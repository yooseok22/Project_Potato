using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkManager))]
public sealed class MultiplayerSessionController : MonoBehaviour
{
    public static MultiplayerSessionController Instance { get; private set; }
    [Tooltip("Assign a prefab with a root NetworkObject. Leave NetworkManager Player Prefab empty; this controller spawns after scene synchronization.")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private string gameScene = "MultiplayerGame";
    [SerializeField] private string menuScene = "MultiplayerMenu";
    private readonly Dictionary<ulong, int> slots = new Dictionary<ulong, int>();
    private NetworkManager network;
    private ISession session;
    private bool leaving;
    private bool quitting;
    private bool connectionLost;
    private bool registeredPrefab;
    public bool Busy { get; private set; }
    public string Code => session?.Code ?? string.Empty;
    public bool InSession => session != null;
    public string Status { get; private set; } = "Create a session or enter a code to join.";
    public event Action Changed;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Application.runInBackground = true;
        DontDestroyOnLoad(gameObject);
        network = GetComponent<NetworkManager>();
        network.OnServerStarted += OnServerStarted;
        network.OnClientDisconnectCallback += OnDisconnected;
    }

    public async void CreateSession() => await ConnectAsync(true, null);
    public async void JoinSession(string code) => await ConnectAsync(false, code);

    private async Task ConnectAsync(bool host, string code)
    {
        if (Busy || InSession || leaving) return;
        code = code?.Trim().ToUpperInvariant();
        if (!host && string.IsNullOrWhiteSpace(code)) return;
        Busy = true;
        connectionLost = false;
        SetStatus(host ? "Creating session..." : "Joining session...");
        try
        {
            if (network.IsListening || network.ShutdownInProgress)
                throw new InvalidOperationException("The previous connection is still closing. Please try again.");
            if (playerPrefab && !playerPrefab.TryGetComponent<NetworkObject>(out _))
                throw new InvalidOperationException("Player Prefab needs a NetworkObject on its root.");
            if (playerPrefab && !registeredPrefab)
            {
                network.AddNetworkPrefab(playerPrefab);
                registeredPrefab = true;
            }
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            if (!this || quitting) return;
            session = host
                ? await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions
                  { MaxPlayers = 4, IsPrivate = true, Name = "Project Potato", Type = "potato-coop" }.WithRelayNetwork())
                : await MultiplayerService.Instance.JoinSessionByCodeAsync(code, new JoinSessionOptions { Type = "potato-coop" });
            if (!this || quitting) return;
            session.RemovedFromSession += OnSessionEnded;
            session.Deleted += OnSessionEnded;
            if (connectionLost) throw new InvalidOperationException("Connection lost while joining. Please try again.");
            if (host)
            {
                var result = network.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);
                if (result != SceneEventProgressStatus.Started)
                    throw new InvalidOperationException("Could not load the game scene: " + result);
            }
            SetStatus(playerPrefab ? "Connected." : "Connected. Player Prefab is not assigned yet.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Multiplayer connection failed: " + exception);
            await CleanupAsync();
            if (this && !quitting) SetStatus("Connection failed: " + exception.Message);
        }
        finally
        {
            Busy = false;
            if (this && !quitting) Changed?.Invoke();
        }
    }

    private void OnServerStarted() => network.SceneManager.OnSceneEvent += OnSceneEvent;

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (!network.IsServer) return;
        if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted && sceneEvent.SceneName == gameScene)
        {
            foreach (var clientId in sceneEvent.ClientsThatCompleted) SpawnPlayer(clientId);
        }
        else if (sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete)
            SpawnPlayer(sceneEvent.ClientId);
    }

    private void SpawnPlayer(ulong clientId)
    {
        if (!playerPrefab || slots.ContainsKey(clientId) || !network.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject) return;
        var spawnPoints = FindFirstObjectByType<MultiplayerSpawnPoints>();
        if (!spawnPoints || spawnPoints.gameObject.scene.name != gameScene) return;
        var points = spawnPoints.Points;
        for (int slot = 0; slot < points.Length; slot++)
        {
            if (!points[slot] || slots.ContainsValue(slot)) continue;
            var player = Instantiate(playerPrefab, points[slot].position, points[slot].rotation);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            slots.Add(clientId, slot);
            return;
        }
        Debug.LogError("No free multiplayer spawn point for client " + clientId);
    }

    private void OnDisconnected(ulong clientId)
    {
        slots.Remove(clientId);
        if (quitting || leaving || clientId != network.LocalClientId) return;
        connectionLost = true;
        if (!Busy) LeaveWithMessage("Disconnected from the host. Create or join another session.");
    }

    private void OnSessionEnded()
    {
        connectionLost = true;
        if (!Busy && !leaving && !quitting) LeaveWithMessage("The session has ended.");
    }

    public void LeaveSession()
    {
        if (!Busy) LeaveWithMessage("Left session. Create or join another session.");
    }

    private async void LeaveWithMessage(string message)
    {
        if (leaving || quitting) return;
        Busy = true;
        Changed?.Invoke();
        await CleanupAsync();
        if (!this || quitting) return;
        if (SceneManager.GetActiveScene().name != menuScene)
            await LoadMenuAsync();
        Busy = false;
        SetStatus(message);
    }

    private async Task LoadMenuAsync()
    {
        var operation = SceneManager.LoadSceneAsync(menuScene);
        while (operation != null && !operation.isDone && this && !quitting) await Task.Yield();
    }

    private async Task CleanupAsync()
    {
        leaving = true;
        var previous = session;
        session = null;
        if (previous != null)
        {
            previous.RemovedFromSession -= OnSessionEnded;
            previous.Deleted -= OnSessionEnded;
            try { await previous.LeaveAsync(); }
            catch (Exception exception) { Debug.LogWarning("Session cleanup: " + exception.Message); }
        }
        if (network)
        {
            if (network.SceneManager != null) network.SceneManager.OnSceneEvent -= OnSceneEvent;
            if (network.IsListening) network.Shutdown();
            while (network && network.ShutdownInProgress && !quitting) await Task.Yield();
        }
        slots.Clear();
        leaving = false;
    }

    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    private void OnApplicationQuit() => quitting = true;
    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (session != null)
        {
            session.RemovedFromSession -= OnSessionEnded;
            session.Deleted -= OnSessionEnded;
        }
        if (!network) return;
        network.OnServerStarted -= OnServerStarted;
        network.OnClientDisconnectCallback -= OnDisconnected;
        if (network.SceneManager != null) network.SceneManager.OnSceneEvent -= OnSceneEvent;
    }
}

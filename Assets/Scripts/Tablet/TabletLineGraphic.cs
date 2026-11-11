using UnityEngine;
using UnityEngine.UI;

/// <summary>Canvas-native line chart, with no additional camera or render texture.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TabletLineGraphic : MaskableGraphic
{
    [SerializeField] private Vector2[] points;
    [SerializeField] private float thickness = 5;
    public void Configure(Vector2[] value, Color tint, float width = 5) { points=value; color=tint; thickness=width; raycastTarget=false; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if(points==null)return;
        for(float x=10;x<rectTransform.rect.width-5;x+=14)for(float y=-10;y>-rectTransform.rect.height;y-=14)
        {
            int k=vh.currentVertCount; Color tint=new Color(.5f,.3f,.18f,.28f);
            vh.AddVert(new Vector2(x-1,y-1),tint,Vector2.zero);vh.AddVert(new Vector2(x-1,y+1),tint,Vector2.zero);
            vh.AddVert(new Vector2(x+1,y+1),tint,Vector2.zero);vh.AddVert(new Vector2(x+1,y-1),tint,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
        }
        for(int i=1;i<points.Length;i++)
        {
            Vector2 a=points[i-1],b=points[i],normal=new Vector2(-(b-a).y,(b-a).x).normalized*thickness*.5f;
            int start=vh.currentVertCount;
            vh.AddVert(a-normal,color,Vector2.zero);vh.AddVert(a+normal,color,Vector2.zero);
            vh.AddVert(b+normal,color,Vector2.zero);vh.AddVert(b-normal,color,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }
    }
}

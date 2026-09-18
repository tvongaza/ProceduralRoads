using System.Collections.Generic;
using UnityEngine;

namespace ProceduralRoads;

/// <summary>Clear colliding natural boulders as loaded zones settle. One loaded
/// heightmap per tick; no retained Unity objects, per-world lists or terrain cache.
/// Networked rocks are destroyed only by their owner. Local scenery is rebuilt
/// on reload and checked again; ZDO destruction persists normally.</summary>
public static class RoadRockClearing
{
    private static float s_next;
    private static int s_cursor;

    public static void Reset() { s_next=0; s_cursor=0; }

    public static void Tick()
    {
        if(!RoadNetworkGenerator.RoadsAvailable || ZoneSystem.instance==null || ZNetScene.instance==null ||
            Time.unscaledTime<s_next) return;
        s_next=Time.unscaledTime+0.5f;
        var maps=Heightmap.GetAllHeightmaps();
        if(maps.Count==0) return;
        if(s_cursor>=maps.Count) s_cursor=0;
        var map=maps[s_cursor++];
        if(map==null) return;
        Vector3 pos=map.transform.position;
        var zone=ZoneSystem.GetZone(pos);
        var points=RoadSpatialGrid.GetRoadPointsInZone(zone);
        if(points.Count==0) return;
        var natural=new HashSet<string>();
        foreach(var veg in ZoneSystem.instance.m_vegetation)
            if(veg.m_enable && veg.m_prefab!=null && (veg.m_biome & RoadRockPolicy.SupportedBiomes)!=0 &&
                RoadRockPolicy.IsNaturalBoulder(veg.m_prefab.name)) natural.Add(veg.m_prefab.name);
        if(natural.Count==0) return;
        var removed=new HashSet<GameObject>();
        foreach(var point in points)
        {
            if(RoadSiteProtection.Contains(point.p)) continue;
            float half=point.w*0.5f;
            // Use the loaded road surface, including any terrain-delta clamp,
            // rather than assuming the planned height was reached exactly.
            if (!map.GetWorldHeight(new Vector3(point.p.x,0,point.p.y),out float surface)) continue;
            // A vertical capsule starts at road height and spans the travelled
            // width. Physics tests real collider geometry, not a centre radius
            // or an axis-aligned box standing in for an irregular boulder.
            var bottom=new Vector3(point.p.x,surface+half,point.p.y);
            var top=bottom+Vector3.up*Mathf.Max(0f,2.5f-2*half);
            foreach(var collider in Physics.OverlapCapsule(bottom,top,half,~0,QueryTriggerInteraction.Ignore))
            {
                if(collider==null) continue;
                Transform? root=collider.transform;
                string name="";
                while(root!=null)
                {
                    name=Utils.GetPrefabName(root.gameObject);
                    if(RoadRockPolicy.IsNaturalBoulder(name)) break;
                    root=root.parent;
                }
                if(root==null || removed.Contains(root.gameObject)) continue;
                var view=root.GetComponent<ZNetView>();
                bool protectedSite=root.GetComponentInParent<Location>()!=null;
                // Protect the whole boulder's collider envelope at a site edge,
                // not only its root, which may stand outside the footprint.
                foreach(var part in root.GetComponentsInChildren<Collider>())
                {
                    var bounds=part.bounds;
                    var centre=new Vector2(bounds.center.x,bounds.center.z);
                    float radius=new Vector2(bounds.extents.x,bounds.extents.z).magnitude;
                    if(RoadSiteProtection.BlocksSegment(centre,centre,radius,null,null)) {protectedSite=true;break;}
                }
                if(!RoadRockPolicy.CanClear(name,natural.Contains(name),Heightmap.FindBiome(root.position),protectedSite,
                    root.GetComponentInParent<Piece>()!=null,view!=null,view!=null && view.IsValid(),
                    view!=null && view.IsOwner())) continue;
                removed.Add(root.gameObject);
                if(view!=null) ZNetScene.instance.Destroy(root.gameObject);
                else Object.Destroy(root.gameObject);
            }
        }
        if(removed.Count>0)
            ProceduralRoadsPlugin.ProceduralRoadsLogger.LogInfo($"Road rocks: cleared {removed.Count} natural boulder(s) in zone {zone}");
    }
}

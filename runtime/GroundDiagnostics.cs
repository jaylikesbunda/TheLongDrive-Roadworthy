using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TldMp
{
    // Ground diagnostics plus the starting-house bus-stop placement. The bridge
    // calls in only when ModDiagnostics.On or loopback is set.
    //  * [TLD MP bus-ground]: one line when a bus stop POI is placed and when it
    //    is spawned, plus a few bounded follow-ups once terrain has had time to
    //    load. Compares the placement height, the alignment-helper heights and
    //    the near-terrain collider under several floor anchors of the real prefab.
    //  * [TLD MP terrain-lod]: while driving, near-collider height vs the distant
    //    meshes' interpolated height at the same global XZ points ahead of the player.
    // The bus-stop half also moves the starting house's stop to a clear roadside
    // spot and releases the house's props once that is done (or the deadline
    // passes). Everything else here is read-only.
    public static class GroundDiagnostics
    {
        const int BusStopId = 5;
        const int MaxBusLines = 400;
        const int MaxPending = 24;
        const int MaxPoiLines = 300;
        const int MaxPlacedChecks = 400;
        const int MaxPoiNearLines = 120;
        const int PoiNearEntries = 8;
        const float PoiNearRange = 150f;
        const float PoiNearMove = 30f;
        const float NearRange = 150f;
        const float PollInterval = .5f;
        const float WaitLimit = 600f;
        const int MaxTilesScanned = 4096;
        const float RayHeight = 1000f;
        const float RayLength = 2000f;
        const float LodInterval = 5f;
        static readonly float[] FollowUps = new float[] { 3f, 10f };
        static readonly float[] LodDistances = new float[] { 0f, 40f, 80f, 120f, 200f, 300f, 450f, 700f };
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        sealed class Pending
        {
            public poiGenScript.poiClass Poi;
            public float Due;
            public int Step;
            public bool Waiting;
            public float Deadline;
        }

        static readonly List<Pending> pending = new List<Pending>();
        static int busLines, poiLines, placedChecks, poiNearLines;
        static Vector3d poiNearAt;
        static bool poiNearSeen;
        static float nextLod, nextPoiNear;
        static bool lodHeader;

        static string F(double x) { return x.ToString("F3", Invariant); }
        static string V(Vector3d p) { return F(p.x) + "," + F(p.y) + "," + F(p.z); }

        static bool Emit(string line, ref int counter, int max)
        {
            if (counter >= max) return false;
            counter++;
            Debug.Log(counter == max ? line + " (log limit reached)" : line);
            return true;
        }

        static mapSettingScript Map()
        {
            return menuhandler.s == null ? null : menuhandler.s.currentMainMap;
        }

        static terrainGenScript NearGen(mapSettingScript map)
        {
            if (map == null || map.terrainGen == null) return null;
            for (int i = 0; i < map.terrainGen.Count; i++)
            {
                var g = map.terrainGen[i];
                if (g != null && g.hasCollider && !g.isDistantTerrain && g.terrains != null) return g;
            }
            return null;
        }

        // The tile whose footprint covers the global XZ point, scanning a bounded number of tiles.
        static newTerrainTileScript TileAt(terrainGenScript gen, Vector3d g, out Vector3d key)
        {
            key = default(Vector3d);
            if (gen == null || gen.terrains == null) return null;
            double half = gen.tileSize * 0.5;
            int scanned = 0;
            foreach (var pair in gen.terrains)
            {
                if (++scanned > MaxTilesScanned) break;
                if (Math.Abs(pair.Key.x - g.x) <= half && Math.Abs(pair.Key.z - g.z) <= half && pair.Value != null)
                { key = pair.Key; return pair.Value; }
            }
            return null;
        }

        // Collider height of the near terrain at a global XZ. tile/collider tell why a miss happened.
        static double ColliderY(terrainGenScript gen, Vector3d g, out bool tile, out bool collider)
        {
            tile = collider = false;
            Vector3d key;
            var t = TileAt(gen, g, out key);
            if (t == null) return double.NaN;
            tile = true;
            if (t.mc == null || !t.mc.enabled || t.mc.sharedMesh == null) return double.NaN;
            collider = true;
            Vector3 origin = mainscript.UnityPosFromGlobal(g) + Vector3.up * RayHeight;
            RaycastHit hit;
            if (!t.mc.Raycast(new Ray(origin, Vector3.down), out hit, RayLength)) return double.NaN;
            return mainscript.GlobalFromUnityPos(hit.point).y;
        }

        // Bilinear height of a tile's render mesh at a global XZ (grid vertices, not the exact triangle split).
        static double MeshY(terrainGenScript gen, Vector3d g)
        {
            Vector3d key;
            var t = TileAt(gen, g, out key);
            if (t == null || t.mf == null || t.mf.sharedMesh == null || gen.resolution < 2 || gen.vertexDist <= 0f) return double.NaN;
            try
            {
                Vector3[] v = t.mf.sharedMesh.vertices;
                int res = gen.resolution;
                if (v == null || v.Length < res * res) return double.NaN;
                double half = gen.tileSize * 0.5;
                double fx = Math.Max(0, Math.Min(res - 1.0001, (g.x - key.x + half) / gen.vertexDist));
                double fz = Math.Max(0, Math.Min(res - 1.0001, (g.z - key.z + half) / gen.vertexDist));
                int j = (int)fx, i = (int)fz;
                double ax = fx - j, az = fz - i;
                double y = (v[j + i * res].y * (1 - ax) + v[j + 1 + i * res].y * ax) * (1 - az)
                         + (v[j + (i + 1) * res].y * (1 - ax) + v[j + 1 + (i + 1) * res].y * ax) * az;
                double lx = fx * gen.vertexDist - half, lz = fz * gen.vertexDist - half;
                return mainscript.GlobalFromUnityPos(t.t.TransformPoint(new Vector3((float)lx, (float)y, (float)lz))).y;
            }
            catch { return double.NaN; }
        }

        static string Num(double v) { return double.IsNaN(v) ? "n/a" : F(v); }

        // The native loading element belongs to the scene. Keep it visible on
        // entering a game while nearby collision and POI contents are prepared.
        static menuhandler loadingOwner;
        static bool loadingFinished;
        static float loadingStarted,loadingReadySince,loadingNextCheck;
        const float LoadingTimeout=30f;

        public static void TickLoadingOverlay(bool multiplayerReady)
        {
            var menu=menuhandler.s;var data=DataFromMenuScript.s;
            if(menu==null || data==null || data.mainmenu)
            {
                // A scene transition may be using this same native element.
                // Do not hide the game's own transition overlay.
                loadingOwner=null;loadingFinished=false;return;
            }
            if(loadingOwner!=menu)
            {
                loadingOwner=menu;loadingFinished=false;loadingStarted=Time.realtimeSinceStartup;
                loadingReadySince=0;loadingNextCheck=0;
                if(menu.GLoadText!=null)menu.GLoadText.SetActive(true);
            }
            if(loadingFinished || menu.GLoadText==null)return;
            float now=Time.realtimeSinceStartup;
            if(now-loadingStarted>=LoadingTimeout)
            {
                loadingFinished=true;menu.GLoadText.SetActive(false);
                Debug.Log("[TLD MP loading] overlay released after timeout");return;
            }
            if(now<loadingNextCheck)return;
            loadingNextCheck=now+.25f;
            bool ready=multiplayerReady && LoadingWorldReady(menu);
            if(!ready){loadingReadySince=0;return;}
            if(loadingReadySince==0){loadingReadySince=now;return;}
            if(now-loadingReadySince<1f)return;
            loadingFinished=true;menu.GLoadText.SetActive(false);
            Debug.Log("[TLD MP loading] nearby world ready after "+F(now-loadingStarted)+"s");
        }

        public static void LoadingOverlayFailed(Exception error)
        {
            if(loadingFinished)return;
            loadingFinished=true;
            if(loadingOwner!=null && loadingOwner.GLoadText!=null)loadingOwner.GLoadText.SetActive(false);
            Debug.LogWarning("[TLD MP loading] overlay released after error: "+error.GetType().Name);
        }

        static bool LoadingWorldReady(menuhandler menu)
        {
            if(menu==null || menu.currentMainMap==null || mainscript.s==null || mainscript.s.player==null)return false;
            var map=menu.currentMainMap;var gen=NearGen(map);
            var position=mainscript.GlobalFromUnityPos(mainscript.s.player.transform.position);
            bool tile,collider;double ground=ColliderY(gen,position,out tile,out collider);
            if(double.IsNaN(ground) || double.IsInfinity(ground))return false;
            if(map.poiGens==null || itemPlaceRemoveScript.s==null)return false;
            foreach(var pois in map.poiGens)
            {
                if(pois==null || !pois.isActiveAndEnabled)continue;
                if(pois.placeStartHouse && !pois.startHouseStarted)return false;
                if(pois.chunks==null || pois.chunkSize<=0)return false;
                poiGenScript.chunkClass center;
                if(!pois.chunks.TryGetValue(mainscript.GetChunkPos(position,pois.chunkSize),out center) || center==null || !center.generated)return false;
                double range=itemPlaceRemoveScript.s.itemSpawnDist;
                foreach(var chunk in pois.chunks)
                {
                    if(chunk.Value==null || chunk.Value.pois==null)continue;
                    foreach(var entry in chunk.Value.pois)
                    {
                        var poi=entry.Value;if(poi==null)continue;
                        double x=poi.pos.x-position.x,z=poi.pos.z-position.z;
                        if(x*x+z*z>=range*range)continue;
                        if(poi.pobj==null || !poi.pobj.gameObject.activeSelf || !poi.spawnedItems || !poi.spawnedFixed)return false;
                    }
                }
            }
            return true;
        }

        // A spawned haz02 can precede its terrain tile. Keep its bus stop pending
        // until the actual near-terrain collider is ready; only then may its
        // detached item spawners run. Entries are removed when the POI despawns.
        static readonly List<poiScript> busPlacementPending = new List<poiScript>();
        static readonly HashSet<poiScript> busPlacementComplete = new HashSet<poiScript>();
        static readonly Dictionary<poiScript, float> busPlacementStarted = new Dictionary<poiScript, float>();
        static float nextBusPlacement;
        static string busPlacementReason = "road data unavailable";
        // The roadside search must finish before the stop's detached props spawn,
        // or they stay behind on the road. It runs every frame, so a few seconds
        // is plenty of attempts; this deadline only guarantees props appear.
        const float SpawnDeadline = 5f;

        static Transform StartingBusMarker(poiScript script)
        {
            if (script == null || script.gameObject.name != "haz02(Clone)") return null;
            var helpers = script.GetComponentsInChildren<terrainHeightAlignToBuildingScript>(true);
            foreach (var helper in helpers)
                if (helper != null && helper.transform.parent != null && helper.transform.parent.name == "Buszmegallo")
                    return helper.transform;
            return null;
        }

        public static void QueueBusPlacement(poiScript script)
        {
            var marker = StartingBusMarker(script);
            if (marker == null || busPlacementComplete.Contains(script)) return;
            if (!busPlacementPending.Contains(script)) busPlacementPending.Add(script);
            if (!busPlacementStarted.ContainsKey(script)) busPlacementStarted[script] = Time.unscaledTime;
            TryBusPlacement(script);
        }

        // True when the house's own props may spawn. They wait for the bus stop to
        // reach its roadside spot so nothing is left behind on the road; a short
        // deadline guarantees they appear promptly even if the search fails.
        public static bool HouseSpawnReady(poiScript script)
        {
            if (script == null) return true;
            if (StartingBusMarker(script) == null) return true;
            float started;
            if (!busPlacementStarted.TryGetValue(script, out started))
                busPlacementStarted[script] = started = Time.unscaledTime;
            if (Time.unscaledTime - started < SpawnDeadline) return false;
            // Stop moving the stop now: its props are about to spawn at whatever
            // position it has, and must not be left behind by a later move.
            busPlacementComplete.Add(script);
            busPlacementPending.Remove(script);
            busPlacementStarted.Remove(script);
            Debug.Log("[TLD MP bus-placement] props released after " + F(Time.unscaledTime - started) + "s; reason=" + busPlacementReason);
            return true;
        }

        public static bool TryBusPlacement(poiScript script)
        {
            if (script == null) return true;
            if (busPlacementComplete.Contains(script)) return true;
            var marker = StartingBusMarker(script);
            if (marker == null) return true;
            var gen = NearGen(Map());
            var global = mainscript.GlobalFromUnityPos(marker.position);
            Vector3d destination;
            bool roadsideReady=StartingStopDestination(script,marker,gen,global,out destination);
            if(!roadsideReady)destination=global;
            bool tile, collider;
            double ground = ColliderY(gen, destination, out tile, out collider);
            if (double.IsNaN(ground) || double.IsInfinity(ground)) { busPlacementReason="terrain unavailable at stop"; return false; }
            var stop = marker.parent;
            var position = stop.position;
            position += new Vector3((float)(destination.x-global.x),(float)(ground-global.y),(float)(destination.z-global.z));
            stop.position = position;
            if(!roadsideReady)return false;
            busPlacementComplete.Add(script);
            busPlacementPending.Remove(script);
            busPlacementStarted.Remove(script);
            Debug.Log("[TLD MP bus-placement] placed marker=" + F(global.y) + " ground=" + F(ground) + " moved=" + F(Math.Sqrt(Math.Pow(destination.x-global.x,2)+Math.Pow(destination.z-global.z,2))) + "m");
            return true;
        }

        // Only the child of the starting house moves. Its house/POI/save keys
        // stay unchanged, and placement completes before detached spawners run.
        static bool StartingStopDestination(poiScript house,Transform marker,terrainGenScript gen,Vector3d original,out Vector3d result)
        {
            result=original; var stop=marker.parent;
            Bounds bounds=new Bounds(stop.position,Vector3.zero); bool found=false;
            foreach(var renderer in stop.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer==null)continue;
                var rendered=renderer.bounds;
                // Unity can report an empty world-origin bound for inactive
                // renderers. It is not geometry and must not expand the stop.
                if(rendered.extents.x<=0 && rendered.extents.z<=0)continue;
                if(!found){bounds=rendered;found=true;}else bounds.Encapsulate(rendered);
            }
            // These positions are used before the detached objects exist. The
            // margin is conservative, but is not a measurement of unknown prefabs.
            foreach(var spawner in stop.GetComponentsInChildren<newRandomStuffSpawnScript>(true))
            {
                if(spawner==null || spawner.items==null)continue;
                foreach(var item in spawner.items)
                {
                    if(item==null || item.places==null)continue;
                    foreach(var place in item.places)
                    {
                        if(place==null || place.T==null)continue;
                        var box=new Bounds(place.T.position,Vector3.one*4f);
                        if(!found){bounds=box;found=true;}else bounds.Encapsulate(box);
                    }
                }
            }
            if(!found){busPlacementReason="missing stop footprint";return false;}
            var roads=mapSettingScript.RoadsNear(original); Vector3d nearest;
            if(roads==null || roads.Count==0){busPlacementReason="roads unavailable";return false;}
            var road=roadGenScript.NearestPointOnRoadBone(original,roads,out nearest);
            if(road==null || !road.generated){busPlacementReason="road not generated";return false;}
            // Use the normal of the nearest actual road segment. A vector to
            // a coarse road-bone result can point almost along the road and
            // demand a huge movement before the footprint leaves the asphalt.
            double best=double.MaxValue,nx=0,nz=0,side=0;
            foreach(var element in road.roadElements)
            {
                if(element==null || element.bonePos==null)continue;
                for(int i=1;i<element.bonePos.Count;i++)
                {
                    var a=element.bonePos[i-1];var b=element.bonePos[i];
                    double dx=b.x-a.x,dz=b.z-a.z,len=dx*dx+dz*dz;
                    if(len<1e-9)continue;
                    double t=Math.Max(0,Math.Min(1,((original.x-a.x)*dx+(original.z-a.z)*dz)/len));
                    double ex=original.x-a.x-t*dx,ez=original.z-a.z-t*dz,d=ex*ex+ez*ez;
                    if(d>=best)continue;
                    best=d;double scale=Math.Sqrt(len);nx=-dz/scale;nz=dx/scale;side=ex*nx+ez*nz;
                }
            }
            if(best==double.MaxValue){busPlacementReason="road has no usable segment";return false;}
            if(Math.Abs(side)<.01)
                side=(marker.position.x-house.transform.position.x)*nx+(marker.position.z-house.transform.position.z)*nz;
            if(side<0){nx=-nx;nz=-nz;}
            Vector3 outward=new Vector3((float)nx,0,(float)nz);
            Bounds home=new Bounds();bool hasHome=false;
            foreach(var renderer in house.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer==null || renderer.transform.IsChildOf(stop))continue;
                if(!hasHome){home=renderer.bounds;hasHome=true;}else home.Encapsulate(renderer.bounds);
            }
            double move=0;
            // Test the actual baked road triangles against the projected stop
            // footprint, rather than estimating a width from road bones.
            for(int pass=0;pass<16;pass++)
            {
                var candidate=original+new Vector3d(outward.x*move,0,outward.z*move);
                var nearby=mapSettingScript.RoadsNear(candidate);
                if(nearby==null || nearby.Count==0){busPlacementReason="roads unavailable at destination";return false;}
                double advance=0;
                foreach(var line in nearby)
                {
                    if(line==null || !line.generated){busPlacementReason="road not generated";return false;}
                    foreach(var element in line.roadElements)
                    {
                        if(element==null || element.bonePos==null || element.bonePos.Count<2)continue;
                        bool near=false;
                        foreach(var bone in element.bonePos)if(Math.Abs(bone.x-candidate.x)<150 && Math.Abs(bone.z-candidate.z)<150){near=true;break;}
                        if(!near)continue;
                        var model=element.obj==null?null:element.obj.GetComponent<roadscript>();
                        if(model==null || model.bakedMesh==null || model.skinnedMeshRenderer==null){busPlacementReason="road mesh unavailable";return false;}
                        var vertices=model.bakedMesh.vertices;var triangles=model.bakedMesh.triangles;
                        if(vertices==null || vertices.Length==0 || triangles==null || triangles.Length<3 || triangles.Length%3!=0){busPlacementReason="road mesh empty";return false;}
                        double cx=candidate.x+bounds.center.x-marker.position.x,cz=candidate.z+bounds.center.z-marker.position.z;
                        for(int i=0;i<triangles.Length;i+=3)
                        {
                            int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];
                            if(ia<0 || ib<0 || ic<0 || ia>=vertices.Length || ib>=vertices.Length || ic>=vertices.Length){busPlacementReason="invalid road mesh";return false;}
                            var a=mainscript.GlobalFromUnityPos(model.skinnedMeshRenderer.transform.TransformPoint(vertices[ia]));
                            var b=mainscript.GlobalFromUnityPos(model.skinnedMeshRenderer.transform.TransformPoint(vertices[ib]));
                            var c=mainscript.GlobalFromUnityPos(model.skinnedMeshRenderer.transform.TransformPoint(vertices[ic]));
                            double exit;
                            if(RoadTriangleExit(a,b,c,cx,cz,bounds.extents.x+1,bounds.extents.z+1,outward.x,outward.z,out exit))
                                advance=Math.Max(advance,exit+.05);
                        }
                    }
                }
                if(advance>0)
                {
                    move+=advance;
                    if(move>80){busPlacementReason="road clearance too far move="+F(move)+" footprint="+F(bounds.extents.x*2)+"x"+F(bounds.extents.z*2)+" direction="+F(outward.x)+","+F(outward.z);return false;}
                    continue;
                }
                Vector3 world=mainscript.UnityPosFromGlobal(candidate);
                if(hasHome && world.x+bounds.center.x-marker.position.x+bounds.extents.x>home.min.x && world.x+bounds.center.x-marker.position.x-bounds.extents.x<home.max.x && world.z+bounds.center.z-marker.position.z+bounds.extents.z>home.min.z && world.z+bounds.center.z-marker.position.z-bounds.extents.z<home.max.z){busPlacementReason="house blocks destination";return false;}
                bool tile,collider;double ground=ColliderY(gen,candidate,out tile,out collider);
                if(double.IsNaN(ground) || double.IsInfinity(ground)){busPlacementReason="terrain unavailable at destination";return false;}
                result=candidate;busPlacementReason="placed";return true;
            }
            busPlacementReason="road clearance did not converge";return false;
        }

        // Swept rectangle versus a road triangle in XZ. The minimum exit among
        // separating axes is the distance needed to leave this triangle.
        static bool RoadTriangleExit(Vector3d a,Vector3d b,Vector3d c,double cx,double cz,double ex,double ez,double vx,double vz,out double exit)
        {
            exit=double.MaxValue;
            double[] xs={1,0,b.z-a.z,c.z-b.z,a.z-c.z};
            double[] zs={0,1,a.x-b.x,b.x-c.x,c.x-a.x};
            for(int i=0;i<xs.Length;i++)
            {
                double x=xs[i],z=zs[i];
                if(x*x+z*z<1e-12)continue;
                double pa=a.x*x+a.z*z,pb=b.x*x+b.z*z,pc=c.x*x+c.z*z;
                double min=Math.Min(pa,Math.Min(pb,pc)),max=Math.Max(pa,Math.Max(pb,pc));
                double center=cx*x+cz*z,reach=Math.Abs(x)*ex+Math.Abs(z)*ez;
                if(center+reach<min || center-reach>max)return false;
                double speed=vx*x+vz*z;
                if(speed>1e-9)exit=Math.Min(exit,(max-(center-reach))/speed);
                else if(speed< -1e-9)exit=Math.Min(exit,((center+reach)-min)/-speed);
            }
            return exit!=double.MaxValue && !double.IsNaN(exit) && !double.IsInfinity(exit);
        }

        public static void TickBusPlacement()
        {
            if (Time.unscaledTime < nextBusPlacement) return;
            nextBusPlacement = Time.unscaledTime + PollInterval;
            for (int i = busPlacementPending.Count - 1; i >= 0; i--)
            {
                var script = busPlacementPending[i];
                if (script == null) { busPlacementPending.RemoveAt(i); continue; }
                TryBusPlacement(script);
            }
            busPlacementComplete.RemoveWhere(script => script == null);
            if (busPlacementStarted.Count > 0)
            {
                List<poiScript> gone = null;
                foreach (var pair in busPlacementStarted) if (pair.Key == null) (gone ?? (gone = new List<poiScript>())).Add(pair.Key);
                if (gone != null) for (int i = 0; i < gone.Count; i++) busPlacementStarted.Remove(gone[i]);
            }
        }

        // Hook: poiGenScript.AddPoi(Vector3d, Vector3, int, bool) entry. The position is the height placement used.
        public static void Placed(Vector3d pos, Vector3 rot, int id, bool aligned)
        {
            bool bus = id == BusStopId;
            if (bus ? busLines >= MaxBusLines : (poiLines >= MaxPoiLines || placedChecks >= MaxPlacedChecks)) return;
            try
            {
                if (!bus) placedChecks++;
                var map = Map();
                if (map == null || map.terrainGenSettings == null || map.terrainGenSettings.Count == 0 || map.terrainGenSettings[0] == null) return;
                var s = map.terrainGenSettings[0];
                double baseY = s.Height(pos);
                double roadY = s.Height(pos, mapSettingScript.RoadsNear(pos));
                // Other POI types only when the placement height and natural ground disagree noticeably.
                if (!bus && Math.Abs(roadY - baseY) < 2.0 && Math.Abs(pos.y - baseY) < 2.0) return;
                var sb = new StringBuilder(256);
                sb.Append(bus ? "[TLD MP bus-ground]" : "[TLD MP poi-ground]").Append(" phase=placed frame=").Append(Time.frameCount).Append(" id=").Append(id);
                sb.Append(" logical=").Append(V(pos)).Append(" rotY=").Append(F(rot.y)).Append(" roadAligned=").Append(aligned);
                sb.Append(" baseHeight=").Append(F(baseY)).Append(" roadHeight=").Append(F(roadY));
                if (bus) Emit(sb.ToString(), ref busLines, MaxBusLines); else Emit(sb.ToString(), ref poiLines, MaxPoiLines);
            }
            catch (Exception e) { Debug.Log("[TLD MP bus-ground] placed probe failed: " + e.Message); }
        }

        // Hook: poiScript.FStart(int, Vector3d) entry, called from poiGenScript.Spawn.
        public static void Spawned(poiScript script, int genId, Vector3d posID)
        {
            if (script == null || (busLines >= MaxBusLines && poiLines >= MaxPoiLines)) return;
            try
            {
                var map = Map();
                if (map == null || map.poiGens == null || genId < 0 || genId >= map.poiGens.Count || map.poiGens[genId] == null) return;
                var gen = map.poiGens[genId];
                poiGenScript.poiClass poi = null;
                foreach (var chunk in gen.chunks)
                {
                    if (chunk.Value != null && chunk.Value.pois != null && chunk.Value.pois.TryGetValue(posID, out poi) && poi != null) break;
                    poi = null;
                }
                if (poi == null) return;
                if (poi.id == BusStopId) Capture(poi, "spawn");
                bool queued = false;
                for (int i = 0; i < pending.Count; i++) if (pending[i].Poi == poi) queued = true;
                if (!queued && pending.Count < MaxPending)
                    pending.Add(new Pending { Poi = poi, Due = Time.unscaledTime + PollInterval, Step = 0, Waiting = true, Deadline = Time.unscaledTime + WaitLimit });
            }
            catch (Exception e) { Debug.Log("[TLD MP bus-ground] spawn probe failed: " + e.Message); }
        }

        static void Capture(poiGenScript.poiClass poi, string phase)
        {
            var map = Map();
            var near = NearGen(map);
            var sb = new StringBuilder(640);
            bool bus = poi.id == BusStopId;
            sb.Append(bus ? "[TLD MP bus-ground]" : "[TLD MP poi-ground]").Append(" phase=").Append(phase).Append(" frame=").Append(Time.frameCount);
            sb.Append(" id=").Append(poi.id).Append(" name=").Append(poi.poiName);
            if (poi.pobj != null) sb.Append(" fixed=").Append(poi.pobj.isFixPOI ? 1 : 0);
            sb.Append(" posID=").Append(V(poi.posID)).Append(" logical=").Append(V(poi.pos)).Append(" roadAligned=").Append(poi.alignedToRoad);
            double rootY = double.NaN;
            var pobj = poi.pobj;
            bool spawned = pobj != null;
            sb.Append(" spawned=").Append(spawned ? 1 : 0);
            if (spawned)
            {
                Vector3d root = mainscript.GlobalFromUnityPos(pobj.transform.position);
                rootY = root.y;
                sb.Append(" spawnedPos=").Append(V(root)).Append(" spawnedMinusLogicalY=").Append(F(root.y - poi.pos.y));
            }
            bool tile, coll;
            double c = ColliderY(near, poi.pos, out tile, out coll);
            sb.Append(" nearTile=").Append(tile ? 1 : 0).Append(" nearCollider=").Append(coll ? 1 : 0);
            sb.Append(" logicalColliderY=").Append(Num(c));
            if (map != null && map.terrainGenSettings != null && map.terrainGenSettings.Count > 0 && map.terrainGenSettings[0] != null)
            {
                var ts = map.terrainGenSettings[0];
                var roads = mapSettingScript.RoadsNear(poi.pos);
                sb.Append(" heightNoPoi=").Append(F(ts.Height(poi.pos, roads, null)));
                sb.Append(" heightWithPoi=").Append(F(ts.Height(poi.pos, roads, mapSettingScript.POIsNear(poi.pos, near == null ? 100f : near.tileSize))));
            }
            if (poi.terrainAligns != null)
            {
                for (int i = 0; i < poi.terrainAligns.Count && i < 4; i++)
                {
                    var a = poi.terrainAligns[i];
                    if (a == null) continue;
                    c = ColliderY(near, a.gpos, out tile, out coll);
                    sb.Append(" align").Append(i).Append("=").Append(V(a.gpos)).Append(" radii=").Append(F(a.minRadius)).Append(",").Append(F(a.maxRadius));
                    sb.Append(" align").Append(i).Append("ColliderY=").Append(Num(c)).Append(" align").Append(i).Append("MinusCollider=").Append(Num(a.gpos.y - c));
                    if (i == 0 && map != null && map.terrainGenSettings != null && map.terrainGenSettings.Count > 0 && map.terrainGenSettings[0] != null)
                    {
                        var setting = map.terrainGenSettings[0];
                        var nearby = mapSettingScript.POIsNear(a.gpos, near == null ? 100f : near.tileSize);
                        var roadsAt = mapSettingScript.RoadsNear(a.gpos);
                        var direct = new List<poiGenScript.poiClass>(); direct.Add(poi);
                        sb.Append(" alignLookupContains=").Append(nearby != null && nearby.Contains(poi) ? 1 : 0);
                        sb.Append(" alignRange=").Append(F(poi.maxAlignEffectRange));
                        sb.Append(" alignBase=").Append(F(setting.Height(a.gpos)));
                        sb.Append(" alignDirect=").Append(F(setting.Height(a.gpos, direct)));
                        sb.Append(" alignLookup=").Append(F(setting.Height(a.gpos, nearby)));
                        sb.Append(" alignRoadAndDirect=").Append(F(setting.Height(a.gpos, roadsAt, direct)));
                        sb.Append(" alignRoadAndLookup=").Append(F(setting.Height(a.gpos, roadsAt, nearby)));
                    }
                }
            }
            if (spawned)
            {
                // Real floor anchors: the spawned prefab's own helper transforms and its visual bottom.
                var helpers = pobj.GetComponentsInChildren<terrainHeightAlignToBuildingScript>(true);
                for (int i = 0; helpers != null && i < helpers.Length && i < 4; i++)
                {
                    Vector3d h = mainscript.GlobalFromUnityPos(helpers[i].transform.position);
                    c = ColliderY(near, h, out tile, out coll);
                    sb.Append(" helper").Append(i).Append("=").Append(V(h)).Append(" helper").Append(i).Append("ColliderY=").Append(Num(c)).Append(" helper").Append(i).Append("MinusCollider=").Append(Num(h.y - c));
                }
                var renderers = pobj.GetComponentsInChildren<Renderer>(false);
                bool any = false;
                Bounds b = default(Bounds);
                for (int i = 0; renderers != null && i < renderers.Length && i < 64; i++)
                {
                    if (renderers[i] == null || renderers[i] is ParticleSystemRenderer) continue;
                    if (!any) { b = renderers[i].bounds; any = true; } else b.Encapsulate(renderers[i].bounds);
                }
                if (any)
                {
                    Vector3d bottom = mainscript.GlobalFromUnityPos(new Vector3(b.center.x, b.min.y, b.center.z));
                    c = ColliderY(near, bottom, out tile, out coll);
                    sb.Append(" visualBottom=").Append(V(bottom)).Append(" visualBottomColliderY=").Append(Num(c)).Append(" visualBottomMinusCollider=").Append(Num(bottom.y - c));
                    sb.Append(" visualSize=").Append(F(b.size.x)).Append(",").Append(F(b.size.y)).Append(",").Append(F(b.size.z));
                }
                c = ColliderY(near, mainscript.GlobalFromUnityPos(pobj.transform.position), out tile, out coll);
                sb.Append(" rootColliderY=").Append(Num(c)).Append(" rootMinusCollider=").Append(Num(rootY - c));
            }
            if (bus) Emit(sb.ToString(), ref busLines, MaxBusLines); else Emit(sb.ToString(), ref poiLines, MaxPoiLines);
        }

        // Called from the bridge pulse. Cheap when idle: one time compare.
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (pending.Count > 0)
            {
                var player = mainscript.s == null ? null : mainscript.s.player;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var p = pending[i];
                    if (now < p.Due) continue;
                    if (p.Poi == null || p.Poi.pobj == null || (p.Poi.id == BusStopId ? busLines >= MaxBusLines : poiLines >= MaxPoiLines) || (p.Waiting && now > p.Deadline)) { pending.RemoveAt(i); continue; }
                    try
                    {
                        if (p.Waiting)
                        {
                            p.Due = now + PollInterval;
                            if (player == null) continue;
                            Vector3d pg = mainscript.GlobalFromUnityPos(player.transform.position);
                            double dx = pg.x - p.Poi.pos.x, dz = pg.z - p.Poi.pos.z;
                            bool tile, coll;
                            bool close = dx * dx + dz * dz <= NearRange * NearRange;
                            ColliderY(NearGen(Map()), p.Poi.pos, out tile, out coll);
                            if (!close && !coll) continue;
                            Capture(p.Poi, "hit dist=" + F(Math.Sqrt(dx * dx + dz * dz)));
                            p.Waiting = false; p.Step = 0; p.Due = now + FollowUps[0];
                            continue;
                        }
                        Capture(p.Poi, "follow+" + FollowUps[p.Step].ToString("F1", Invariant) + "s");
                    }
                    catch (Exception e) { Debug.Log("[TLD MP bus-ground] follow-up failed: " + e.Message); pending.RemoveAt(i); continue; }
                    p.Step++;
                    if (p.Step >= FollowUps.Length) pending.RemoveAt(i); else p.Due = now + FollowUps[p.Step] - FollowUps[p.Step - 1];
                }
            }
            if (now >= nextPoiNear) { nextPoiNear = now + LodInterval; try { PoiNear(); } catch (Exception e) { Debug.Log("[TLD MP poi-near] sample failed: " + e.Message); nextPoiNear = now + 30f; } }
            if (now >= nextLod) { nextLod = now + LodInterval; try { SampleLod(); } catch (Exception e) { Debug.Log("[TLD MP terrain-lod] sample failed: " + e.Message); nextLod = now + 30f; } }
        }

        // The POIs nearest the player, once the player has moved far enough since the last line.
        static void PoiNear()
        {
            if (poiNearLines >= MaxPoiNearLines) return;
            var map = Map();
            var player = mainscript.s == null ? null : mainscript.s.player;
            if (map == null || !map.started || player == null || map.poiGens == null) return;
            Vector3d pg = mainscript.GlobalFromUnityPos(player.transform.position);
            if (poiNearSeen)
            {
                double mx = pg.x - poiNearAt.x, mz = pg.z - poiNearAt.z;
                if (mx * mx + mz * mz < PoiNearMove * PoiNearMove) return;
            }
            poiNearSeen = true;
            poiNearAt = pg;
            var near = NearGen(map);
            var best = new List<poiGenScript.poiClass>();
            var bestD = new List<double>();
            for (int gi = 0; gi < map.poiGens.Count; gi++)
            {
                var gen = map.poiGens[gi];
                if (gen == null || gen.chunks == null) continue;
                foreach (var chunk in gen.chunks)
                {
                    if (chunk.Value == null || chunk.Value.pois == null) continue;
                    foreach (var pair in chunk.Value.pois)
                    {
                        var poi = pair.Value;
                        if (poi == null) continue;
                        double dx = poi.pos.x - pg.x, dz = poi.pos.z - pg.z, d2 = dx * dx + dz * dz;
                        if (d2 > PoiNearRange * PoiNearRange) continue;
                        int at = best.Count;
                        while (at > 0 && bestD[at - 1] > d2) at--;
                        if (at >= PoiNearEntries) continue;
                        best.Insert(at, poi); bestD.Insert(at, d2);
                        if (best.Count > PoiNearEntries) { best.RemoveAt(best.Count - 1); bestD.RemoveAt(bestD.Count - 1); }
                    }
                }
            }
            if (best.Count == 0) return;
            var sb = new StringBuilder(512);
            sb.Append("[TLD MP poi-near] frame=").Append(Time.frameCount).Append(" at=").Append(V(pg));
            for (int i = 0; i < best.Count; i++)
            {
                var poi = best[i];
                bool tile, coll;
                double c = ColliderY(near, poi.pos, out tile, out coll);
                sb.Append(" | ").Append(poi.poiName).Append("#").Append(poi.id).Append(" d=").Append(F(Math.Sqrt(bestD[i])));
                sb.Append(" y=").Append(F(poi.pos.y)).Append(" spawned=").Append(poi.pobj != null ? 1 : 0);
                if (poi.pobj != null) sb.Append(" rootY=").Append(F(mainscript.GlobalFromUnityPos(poi.pobj.transform.position).y)).Append(" fixed=").Append(poi.pobj.isFixPOI ? 1 : 0);
                sb.Append(" colliderY=").Append(Num(c)).Append(" yMinusCollider=").Append(Num(poi.pos.y - c));
            }
            Emit(sb.ToString(), ref poiNearLines, MaxPoiNearLines);
        }

        static void SampleLod()
        {
            var map = Map();
            var player = mainscript.s == null ? null : mainscript.s.player;
            if (map == null || !map.started || player == null || map.terrainGen == null) return;
            var near = NearGen(map);
            if (near == null) return;
            Transform body = player.Car != null ? player.Car.transform : player.transform;
            Vector3 fwd = body.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; else fwd.Normalize();
            var far = new List<terrainGenScript>();
            for (int i = 0; i < map.terrainGen.Count; i++)
            {
                var g = map.terrainGen[i];
                if (g != null && g.isDistantTerrain && g.terrains != null) far.Add(g);
            }
            if (!lodHeader)
            {
                lodHeader = true;
                var hs = new StringBuilder("[TLD MP terrain-lod] generators");
                for (int i = 0; i < map.terrainGen.Count; i++)
                {
                    var g = map.terrainGen[i];
                    if (g == null) continue;
                    hs.Append(" ").Append(g.terrainName).Append("{tile=").Append(F(g.tileSize)).Append(",res=").Append(g.resolution)
                      .Append(",vertexDist=").Append(F(g.vertexDist)).Append(",collider=").Append(g.hasCollider ? 1 : 0).Append(",distant=").Append(g.isDistantTerrain ? 1 : 0);
                    if (g.material != null)
                    {
                        string[] props = { "_NearDist", "_FarDist", "_Amount" };
                        foreach (string prop in props) if (g.material.HasProperty(prop)) hs.Append(",").Append(prop).Append("=").Append(F(g.material.GetFloat(prop)));
                    }
                    hs.Append("}");
                }
                Debug.Log(hs.ToString());
            }
            var s = new StringBuilder(256);
            s.Append("[TLD MP terrain-lod] frame=").Append(Time.frameCount).Append(" at=").Append(V(mainscript.GlobalFromUnityPos(body.position)));
            s.Append(" speed=").Append(F(player.Car != null && player.Car.GetComponent<Rigidbody>() != null ? player.Car.GetComponent<Rigidbody>().velocity.magnitude : 0f));
            s.Append(" t=").Append(F(Time.realtimeSinceStartup)).Append(" origin=").Append(V(mainscript.s.visszarakva));
            if (player.Car != null)
            {
                var rb = player.Car.GetComponent<Rigidbody>();
                s.Append(" car=").Append(player.Car.name);
                if (rb != null) s.Append(" velocity=").Append(V(new Vector3d(rb.velocity.x, rb.velocity.y, rb.velocity.z)))
                    .Append(" kinematic=").Append(rb.isKinematic).Append(" collisions=").Append(rb.detectCollisions)
                    .Append(" mode=").Append(rb.collisionDetectionMode);
            }
            Vector3d key;
            var center = mainscript.GlobalFromUnityPos(body.position);
            var tileAtCar = TileAt(near, center, out key);
            s.Append(" centerTile=").Append(tileAtCar != null).Append(" key=").Append(V(key));
            if (tileAtCar != null)
                s.Append(" tileAt=").Append(V(mainscript.GlobalFromUnityPos(tileAtCar.transform.position)))
                    .Append(" collider=").Append(tileAtCar.mc != null && tileAtCar.mc.enabled && tileAtCar.mc.sharedMesh != null)
                    .Append(" active=").Append(tileAtCar.gameObject.activeInHierarchy);
            s.Append(" centerMesh=").Append(Num(MeshY(near, center)));
            var settings = map.terrainGenSettings != null && map.terrainGenSettings.Count > 0 ? map.terrainGenSettings[0] : null;
            for (int d = 0; d < LodDistances.Length; d++)
            {
                Vector3d g = mainscript.GlobalFromUnityPos(body.position + fwd * LodDistances[d]);
                bool tile, coll;
                double ny = ColliderY(near, g, out tile, out coll);
                s.Append(" | d=").Append(F(LodDistances[d])).Append(" base=").Append(settings == null ? "n/a" : F(settings.Height(g)));
                s.Append(" near=").Append(Num(ny));
                for (int i = 0; i < far.Count; i++)
                {
                    double fy = MeshY(far[i], g);
                    s.Append(" ").Append(far[i].terrainName).Append("=").Append(Num(fy));
                    if (i == 0 && !double.IsNaN(fy) && !double.IsNaN(ny)) s.Append("(d").Append(F(fy - ny)).Append(")");
                }
            }
            // Keep a rate-limited record for the entire drive. A lifetime cap
            // used to silence this before later streaming/recenter failures.
            Debug.Log(s.ToString());
        }
    }
}

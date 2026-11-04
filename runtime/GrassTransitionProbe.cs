using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TldMp
{
    public sealed class GrassTransitionProbe : MonoBehaviour
    {
        public string output;
        readonly List<string> lines=new List<string>();
        const int Layer=31;

        void Start(){StartCoroutine(Run());}
        void Log(string s){lines.Add(s);Debug.Log("[grass-probe] "+s);}

        IEnumerator Run()
        {
            System.IO.Directory.CreateDirectory(output);
            float deadline=Time.realtimeSinceStartup+90f;
            while((DataFromMenuScript.s==null||!DataFromMenuScript.s.mainmenu)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(DataFromMenuScript.s==null||!DataFromMenuScript.s.mainmenu){Finish("FAIL no menu within deadline");}
            else if(WorldBiomePolicy.Mode!=0){Finish("FAIL requires world biome mode 0, found "+WorldBiomePolicy.Mode);}
            else
            {
                yield return null;yield return null;
                var it=Body();bool done=false;System.Exception err=null;
                while(!done)
                {
                    try{done=!it.MoveNext();}
                    catch(System.Exception e){err=e;done=true;}
                    if(!done)yield return it.Current;
                }
                if(err!=null)Log("FAIL "+err);
                Finish(err==null?"done":"failed");
            }
            if(System.Environment.GetEnvironmentVariable("TLD_MP_GRASS_PROBE_QUIT")=="1"){yield return null;Application.Quit();}
        }
        void Finish(string tail){lines.Add(tail);try{System.IO.File.WriteAllLines(System.IO.Path.Combine(output,"probe.log"),lines.ToArray());}catch(System.Exception e){Debug.LogError("[grass-probe] "+e);}}

        IEnumerator Body()
        {
            GameObject root=null,camGO=null,lightGO=null;
            RenderTexture rt=null;Texture2D tex=null;
            Material grassMat=null,groundMat=null;
            Mesh sourceMesh=null;
            newTerrainTileScript tile=null;
            var oldRT=RenderTexture.active;
            try
            {
                groundMat=new Material(Shader.Find("Standard"));
                if(groundMat==null||groundMat.shader==null)groundMat=new Material(Shader.Find("Unlit/Color"));
                if(groundMat!=null)groundMat.color=new Color(0.45f,0.3f,0.15f,1f);
                Material picked=null;
                foreach(var m in Resources.FindObjectsOfTypeAll<Material>())
                    if(m!=null&&m.shader!=null&&(m.name=="grassterraingrass"||m.name=="newGrass")&&m.shader.name=="Stix Games/Grass"){picked=m;break;}
                if(picked!=null)grassMat=new Material(picked);
                else
                {
                    var sh=Shader.Find("Stix Games/Grass");
                    if(sh==null){Log("FAIL Stix Games/Grass shader not found");yield break;}
                    grassMat=new Material(sh);
                    foreach(var k in new[]{"GRASS_CALC_GLOBAL_WIND","GRASS_TOP_VIEW_COMPENSATION","GRASS_UNSHADED_LIGHTING","SIMPLE_GRASS","UNIFORM_DENSITY"})grassMat.EnableKeyword(k);
                    grassMat.SetFloat("_MaxTessellation",5f);
                    grassMat.SetFloat("_EdgeLength",15f);
                    grassMat.SetFloat("_GrassFadeEnd",200f);
                    grassMat.SetFloat("_MinHeight00",0.5f);
                    grassMat.SetFloat("_MaxHeight00",1f);
                    grassMat.SetFloat("_Width00",0.12f);
                }
                if(grassMat.shader==null||!grassMat.shader.isSupported){Log("FAIL grass shader unsupported");yield break;}
                if(!grassMat.HasProperty("_MaxTessellation")||!grassMat.HasProperty("_EdgeLength")){Log("FAIL grass shader missing density properties");yield break;}
                root=new GameObject("grass-probe-tile");
                root.layer=Layer;
                root.transform.position=new Vector3(1000f,0f,1000f);
                tile=root.AddComponent<newTerrainTileScript>();
                tile.t=root.transform;
                var mf=root.AddComponent<MeshFilter>();tile.mf=mf;
                var mr=root.AddComponent<MeshRenderer>();tile.mr=mr;
                tile.material=groundMat;
                tile.grassMaterial=grassMat;
                tile.biomeColor=new Color(0f,1f,0f,1f);
                const int N=33;
                var verts=new Vector3[N*N];var cols=new Color[N*N];var norms=new Vector3[N*N];var uvs=new Vector2[N*N];var tris=new List<int>();
                for(int z=0;z<N;z++)for(int x=0;x<N;x++)
                {
                    int i=z*N+x;float u=x/(float)(N-1),v=z/(float)(N-1);
                    verts[i]=new Vector3(u*64f,0f,v*64f);
                    norms[i]=Vector3.up;uvs[i]=new Vector2(u,v);
                    cols[i]=new Color(1f-u,u,0f,1f);
                }
                for(int z=0;z<N-1;z++)for(int x=0;x<N-1;x++)
                {
                    int i=z*N+x;tris.Add(i);tris.Add(i+N);tris.Add(i+1);
                    tris.Add(i+1);tris.Add(i+N);tris.Add(i+N+1);
                }
                sourceMesh=new Mesh();
                sourceMesh.vertices=verts;sourceMesh.colors=cols;sourceMesh.normals=norms;sourceMesh.uv=uvs;
                sourceMesh.SetTriangles(tris,0);
                sourceMesh.RecalculateBounds();
                mf.sharedMesh=sourceMesh;
                tile.UpdMesh(sourceMesh);
                camGO=new GameObject("grass-probe-cam");
                camGO.layer=Layer;
                var cam=camGO.AddComponent<Camera>();
                cam.fieldOfView=60f;cam.farClipPlane=220f;cam.nearClipPlane=0.5f;cam.cullingMask=1<<Layer;
                cam.clearFlags=CameraClearFlags.SolidColor;
                cam.backgroundColor=new Color(0.05f,0.05f,0.08f,1f);
                cam.allowHDR=false;
                cam.transform.position=new Vector3(32f+1000f,42f,-62f+1000f);
                cam.transform.LookAt(new Vector3(32f+1000f,0f,32f+1000f));
                cam.enabled=false;
                rt=new RenderTexture(1280,720,24);
                cam.targetTexture=rt;
                lightGO=new GameObject("grass-probe-light");
                lightGO.layer=Layer;
                var lt=lightGO.AddComponent<Light>();
                lt.type=LightType.Directional;lt.cullingMask=1<<Layer;
                lightGO.transform.rotation=Quaternion.Euler(50f,-30f,0f);
                mr.sharedMaterials=new Material[]{groundMat,grassMat};
                yield return null;yield return null;
                tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
                if(!DataFromMenuScript.s.mainmenu){Log("FAIL left main menu before baseline");yield break;}
                if(!Capture(cam,rt,tex,System.IO.Path.Combine(output,"baseline.png"))){Log("FAIL baseline capture");yield break;}
                if(!DataFromMenuScript.s.mainmenu){Log("FAIL left main menu before candidate");yield break;}
                GrassTransition.Prepare(tile,new Vector3d(0,0,0));
                tile.UpdMaterial(true);
                var st=tile.GetComponent<GrassTransitionState>();
                int nonempty=0;
                if(st!=null&&st.plan!=null)for(int g=1;g<=8;g++)if(st.plan.groupCounts[g]>0)nonempty++;
                if(st==null||st.child==null||nonempty<2){Log("FAIL expected owned grass renderer with >=2 buckets, got "+nonempty);yield break;}
                if(mf.sharedMesh!=sourceMesh){Log("FAIL source mesh reference mutated");yield break;}
                if(sourceMesh.vertices.Length!=N*N||sourceMesh.colors.Length!=N*N){Log("FAIL source attributes changed");yield break;}
                if(!Capture(cam,rt,tex,System.IO.Path.Combine(output,"candidate.png"))){Log("FAIL candidate capture");yield break;}
                if(!DataFromMenuScript.s.mainmenu){Log("FAIL left main menu before grass-off");yield break;}
                tile.UpdMaterial(false);
                yield return null;
                if(!Capture(cam,rt,tex,System.IO.Path.Combine(output,"grass-off.png"))){Log("FAIL grass-off capture");yield break;}
                lines.Add("PASS 3 captures, buckets="+nonempty);
            }
            finally
            {
                RenderTexture.active=oldRT;
                if(root!=null)root.SetActive(false);
                if(camGO!=null&&camGO.GetComponent<Camera>()!=null)camGO.GetComponent<Camera>().targetTexture=null;
                if(rt!=null){rt.Release();Object.Destroy(rt);}
                if(tex!=null)Object.Destroy(tex);
                if(grassMat!=null)Object.Destroy(grassMat);
                if(groundMat!=null)Object.Destroy(groundMat);
                if(sourceMesh!=null)Object.Destroy(sourceMesh);
                if(root!=null)Object.Destroy(root);
                if(camGO!=null)Object.Destroy(camGO);
                if(lightGO!=null)Object.Destroy(lightGO);
            }
        }
        bool Capture(Camera cam,RenderTexture rt,Texture2D tex,string path)
        {
            var before=RenderTexture.active;
            try
            {
                RenderTexture.active=rt;
                cam.Render();
                tex.ReadPixels(new Rect(0,0,1280,720),0,0);
                tex.Apply();
                var png=ImageConversion.EncodeToPNG(tex);
                System.IO.File.WriteAllBytes(path,png);
                lines.Add("captured "+path+" "+png.Length+"B");
                return true;
            }
            catch(System.Exception e){lines.Add("capture error "+e.Message);return false;}
            finally{RenderTexture.active=before;}
        }
    }
}

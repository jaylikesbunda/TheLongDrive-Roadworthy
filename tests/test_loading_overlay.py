"""Exercise the production loading overlay against deterministic world stand-ins."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class LoadingOverlayTests(unittest.TestCase):
    def test_world_readiness_and_scene_lifecycle(self):
        source = (ROOT / 'runtime/GroundDiagnostics.cs').read_text(encoding='utf-8')
        begin = source.index('        static menuhandler loadingOwner;')
        end = source.index('        // A spawned haz02 can precede', begin)
        fixture = r'''
using System;using System.Collections.Generic;
public struct Vector3 {public float x,y,z;public static readonly Vector3 zero=new Vector3();public static readonly Vector3 up=new Vector3{y=1};public static readonly Vector3 down=new Vector3{y=-1};public static Vector3 operator *(Vector3 v,float s){return new Vector3{x=v.x*s,y=v.y*s,z=v.z*s};}public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3{x=a.x+b.x,y=a.y+b.y,z=a.z+b.z};}}
public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
public struct Quaternion {public float x,y,z,w;public static readonly Quaternion identity=new Quaternion{w=1};}
public struct Vector3d {public double x,y,z;}
public class Transform {public Vector3 position;public Quaternion localRotation;public Vector3 localScale;public GameObject gameObject;public Transform findResult;public Transform parent;public readonly List<Transform> children=new List<Transform>();public string name{get{return gameObject==null?null:gameObject.name;}}public Transform Find(string p){return findResult;}public void SetParent(Transform p,bool stays){if(parent!=null)parent.children.Remove(this);parent=p;if(p!=null)p.children.Add(this);}public int GetSiblingIndex(){return parent==null?0:parent.children.IndexOf(this);}public void SetSiblingIndex(int i){if(parent==null)return;var c=parent.children;c.Remove(this);if(i<0)i=0;if(i>c.Count)i=c.Count;c.Insert(i,this);}}
public class RectTransform:Transform {public Vector2 anchorMin,anchorMax,pivot,sizeDelta;public Vector3 anchoredPosition3D;}
public struct Color {public float r,g,b,a;public Color(float rr,float gg,float bb,float aa){r=rr;g=gg;b=bb;a=aa;}}
public class Texture2D {}
public class Sprite {public Texture2D texture;public bool destroyed;public Sprite clonedFrom;static bool Dead(Sprite s){return (object)s==null||s.destroyed;}public static bool operator ==(Sprite a,Sprite b){return ReferenceEquals(a,b)||(Dead(a)&&Dead(b));}public static bool operator !=(Sprite a,Sprite b){return !(a==b);}public override bool Equals(object o){return this==(o as Sprite);}public override int GetHashCode(){return destroyed?0:base.GetHashCode();}}
public class Material {}
public class Image {public Color color;public Sprite sprite;public Material material;public bool preserveAspect;public int type;public bool raycastTarget=true;}
public class GameObject {public bool activeSelf,destroyed;public int layer;public string name;public Transform transform;public Image image;public GameObject(){transform=new RectTransform{gameObject=this};}public GameObject(string n,params Type[] components){name=n;transform=new RectTransform{gameObject=this};foreach(var c in components)if(c==typeof(Image))image=new Image();}public void SetActive(bool b){activeSelf=b;}public T GetComponent<T>(){return (T)(object)image;}}
public static class UnityEngine {public static class Object {public static void Destroy(object o){var g=o as GameObject;if(g!=null){g.destroyed=true;g.activeSelf=false;if(g.transform!=null)g.transform.SetParent(null,false);return;}var s=o as Sprite;if(s!=null)s.destroyed=true;}public static T Instantiate<T>(T source){if(source is Sprite){var sp=(Sprite)(object)source;return (T)(object)new Sprite{texture=sp.texture,clonedFrom=sp};}return source;}}}
public class Rigidbody {public Vector3 velocity,angularVelocity;}
public class Collider {public bool enabled=true;public bool isTrigger;}
public struct RaycastHit {public Collider collider;public Vector3 point;public Vector3 normal;}
public enum QueryTriggerInteraction {Ignore}
public static class Physics {public static RaycastHit[] supportHits=new RaycastHit[0];public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float m,int mask,QueryTriggerInteraction q){return supportHits;}}
public class fpscontroller {public Transform transform=new Transform();public Rigidbody RB=new Rigidbody();public Collider[] C=new Collider[0];}
public class mainscript {public static mainscript s=new mainscript();public fpscontroller player=new fpscontroller();public static Vector3d GlobalFromUnityPos(Vector3 p){return new Vector3d{x=p.x,y=p.y,z=p.z};}public static Vector3d GetChunkPos(Vector3d p,float size){return new Vector3d();}}
public class DataFromMenuScript {public static DataFromMenuScript s=new DataFromMenuScript();public bool mainmenu;}
public class mapSettingScript {public List<poiGenScript> poiGens=new List<poiGenScript>();}
public class menuhandler {public static menuhandler s;public Transform transform=new Transform();public GameObject GLoadText=new GameObject();public GameObject Menu=new GameObject();public mapSettingScript currentMainMap=new mapSettingScript();public Image menuOverlayBG,menuOverlayBGKeret,menuBackGroundImage;}
public class terrainGenScript {}
public class poiScript {public GameObject gameObject=new GameObject();}
public class poiGenScript {public bool isActiveAndEnabled=true,placeStartHouse,startHouseStarted;public float chunkSize=100;public Dictionary<Vector3d,chunkClass> chunks=new Dictionary<Vector3d,chunkClass>();public class chunkClass {public bool generated;public Dictionary<Vector3d,poiClass> pois=new Dictionary<Vector3d,poiClass>();}public class poiClass {public Vector3d pos;public poiScript pobj;public bool spawnedItems,spawnedFixed;}}
public class itemPlaceRemoveScript {public static itemPlaceRemoveScript s=new itemPlaceRemoveScript();public float itemSpawnDist=100;}
public static class Time {public static float realtimeSinceStartup;}
public static class Debug {public static void Log(string s){}public static void LogWarning(string s){}}
public static class Adapter {
static bool hasGround;
static void SetSupport(bool v){Physics.supportHits=v?new[]{new RaycastHit{normal=new Vector3{y=1},collider=new Collider()}}:new RaycastHit[0];}
public static void Ground(bool v){hasGround=v;SetSupport(v);}
public static void Support(bool v){SetSupport(v);}
static terrainGenScript NearGen(mapSettingScript map){return new terrainGenScript();}
static double ColliderY(terrainGenScript g,Vector3d p,out bool tile,out bool collider){tile=collider=hasGround;return hasGround?0:double.NaN;}
static string F(double x){return x.ToString();}
METHOD
}
public static class Test {
static void Check(bool v,string s){if(!v)throw new Exception(s);}
static void Tick(float t,bool live){Time.realtimeSinceStartup=t;Adapter.TickLoadingOverlay(live);}
static Transform Kid(Transform p,string n){if(p==null)return null;foreach(var c in p.children)if(c.name==n)return c;return null;}
public static void Main(){
var menu=new menuhandler();menuhandler.s=menu;var gen=new poiGenScript{placeStartHouse=true};menu.currentMainMap.poiGens.Add(gen);var chunk=new poiGenScript.chunkClass();gen.chunks.Add(new Vector3d(),chunk);
var bg=new GameObject();bg.layer=28;bg.image=new Image{sprite=null,color=new Color(0f,0f,0f,0.588f)};menu.GLoadText.transform.findResult=bg.transform;bg.transform.SetParent(menu.GLoadText.transform,false);
var bgRect=(RectTransform)bg.transform;bgRect.anchorMax=new Vector2(1f,1f);bgRect.sizeDelta=new Vector2(10f,20f);bgRect.anchoredPosition3D=new Vector3{x=1,y=2,z=3};bgRect.localScale=new Vector3{x=1,y=1,z=1};bgRect.localRotation=Quaternion.identity;
Tick(1,true);Check(menu.GLoadText.activeSelf,"initial overlay shown");Check(bg.activeSelf,"loading background shown");Check(bg.image.sprite==null && bg.image.color.a==0.588f,"native dimmer kept, no art pasted over it");
var baseGO=Kid(menu.GLoadText.transform,"TldMpLoadingBase").gameObject;Check(baseGO!=null,"opaque base layer added");Check(Kid(menu.GLoadText.transform,"TldMpLoadingPaper")==null && Kid(menu.GLoadText.transform,"TldMpLoadingFrame")==null,"missing paper and border sources omit their layers");
Check(baseGO.transform.GetSiblingIndex()<bg.transform.GetSiblingIndex(),"base sits under the dimmer");
var baseImg=baseGO.GetComponent<Image>();Check(baseImg.sprite==null && baseImg.color.a==1f && baseImg.color.r==0.9056604f && baseImg.color.b==0.6792453f,"authored warm opaque base when nothing is known");Check(baseGO.layer==28,"layer inherits the panel's UI layer");
var baseRect=(RectTransform)baseGO.transform;Check(baseRect.anchorMax.x==1f && baseRect.sizeDelta.y==20f && baseRect.anchoredPosition3D.z==3f && baseRect.localScale.x==1f,"layers copy the dimmer's coverage");
Adapter.Ground(true);Tick(2,true);Check(menu.GLoadText.activeSelf,"wait for starting house");
gen.startHouseStarted=true;Tick(3,true);Check(menu.GLoadText.activeSelf,"wait for generated chunk");chunk.generated=true;
var poi=new poiGenScript.poiClass{pobj=new poiScript()};poi.pobj.gameObject.activeSelf=true;chunk.pois.Add(new Vector3d(),poi);
Tick(4,true);Check(menu.GLoadText.activeSelf,"wait for POI props");poi.spawnedItems=true;poi.spawnedFixed=true;
Tick(5,false);Check(menu.GLoadText.activeSelf,"wait for guest spawn");
menu.GLoadText.SetActive(false);bg.SetActive(false);bg.image.sprite=new Sprite();bg.image.color=new Color(1f,1f,1f,0f);baseGO.SetActive(false);baseImg.sprite=new Sprite();baseImg.color=new Color(0f,0f,0f,0f);baseGO.transform.SetSiblingIndex(1);
Tick(5.1f,true);Check(menu.GLoadText.activeSelf,"native hide repaired during hold");Check(bg.activeSelf && bg.image.sprite==null && bg.image.color.a==0.588f,"dimmer restored before the next poll");Check(baseGO.activeSelf && baseImg.sprite==null && baseImg.color.a==1f,"base layer repaired before the next poll");Check(baseGO.transform.GetSiblingIndex()<bg.transform.GetSiblingIndex(),"layer order repaired");
Tick(6,true);Tick(6.5f,true);Check(menu.GLoadText.activeSelf,"wait for stability");Tick(7.1f,true);Check(!menu.GLoadText.activeSelf,"ready overlay hidden");Check(!bg.activeSelf && bg.image.sprite==null && bg.image.color.a==0.588f,"dimmer sprite and color restored");Check(baseGO.destroyed && !baseGO.activeSelf,"base layer removed on release");
var texA=new Texture2D();var directShot=new Sprite{texture=texA};var directPaper=new Sprite();var directFrame=new Sprite();var directMat=new Material();
var direct=new menuhandler();direct.menuBackGroundImage=new Image{sprite=directShot,color=new Color(0.6f,0.6f,0.6f,1f)};direct.menuOverlayBG=new Image{sprite=directPaper,color=new Color(0.4f,0.4f,0.4f,0.2f),material=directMat,preserveAspect=true,type=3};direct.menuOverlayBGKeret=new Image{sprite=directFrame,color=new Color(1f,1f,1f,1f)};
var bgd=new GameObject();bgd.layer=28;bgd.image=new Image{sprite=null,color=new Color(0f,0f,0f,0.588f)};direct.GLoadText.transform.findResult=bgd.transform;bgd.transform.SetParent(direct.GLoadText.transform,false);
menuhandler.s=direct;Tick(7.5f,true);
Check(direct.GLoadText.activeSelf && bgd.activeSelf,"direct play overlay shown");
var dBase=Kid(direct.GLoadText.transform,"TldMpLoadingBase");var dPaper=Kid(direct.GLoadText.transform,"TldMpLoadingPaper");var dFrame=Kid(direct.GLoadText.transform,"TldMpLoadingFrame");
Check(dBase!=null && dPaper!=null && dFrame!=null,"direct play builds the full native stack");
Check(dBase.GetSiblingIndex()<dPaper.GetSiblingIndex() && dPaper.GetSiblingIndex()<dFrame.GetSiblingIndex() && dFrame.GetSiblingIndex()<bgd.transform.GetSiblingIndex(),"base, paper, border then dimmer");
var dPaperImg=dPaper.gameObject.GetComponent<Image>();Check(dPaperImg.sprite==directPaper && dPaperImg.color.a==1f && dPaperImg.color.r==0.4f,"paper layer draws at full alpha with the source RGB");Check(direct.menuOverlayBG.color.a==0.2f,"native paper source keeps its translucency");Check(dPaperImg.material==directMat && dPaperImg.preserveAspect && dPaperImg.type==3 && !dPaperImg.raycastTarget,"layer copies image fidelity and never blocks rays");
var dBaseImg=dBase.gameObject.GetComponent<Image>();Check(dBaseImg.sprite==directShot && dBaseImg.color.a==1f,"direct play uses the live base source");Check(bgd.image.sprite==null && bgd.image.color.a==0.588f,"dimmer untouched under the layers");Check(dBase.gameObject.layer==28 && dPaper.gameObject.layer==28 && dFrame.gameObject.layer==28,"every layer inherits the panel's UI layer");
direct.Menu.SetActive(true);Tick(7.8f,true);
direct.Menu.SetActive(false);
var post=new menuhandler();menuhandler.s=post;var pbg=new GameObject();pbg.layer=28;pbg.image=new Image{sprite=null,color=new Color(0f,0f,0f,0.588f)};post.GLoadText.transform.findResult=pbg.transform;pbg.transform.SetParent(post.GLoadText.transform,false);
Tick(7.9f,true);Check(!bgd.activeSelf && bgd.image.color.a==0.588f,"owner switch restored the previous dimmer");Check(dBase.gameObject.destroyed && dPaper.gameObject.destroyed && dFrame.gameObject.destroyed,"owner switch removed the old layers");
var pBase=Kid(post.GLoadText.transform,"TldMpLoadingBase");var pPaper=Kid(post.GLoadText.transform,"TldMpLoadingPaper");
Check(pPaper!=null && pPaper.gameObject.GetComponent<Image>().sprite==directPaper,"outgoing-transition capture carries the picked paper");
var pBaseImg=pBase.gameObject.GetComponent<Image>();Check(pBaseImg.sprite!=directShot && pBaseImg.sprite.texture==texA,"base keeps an owned screenshot clone");
var texB=new Texture2D();var shotSprite=new Sprite{texture=texB};var nativeArt=new Sprite();var keretArt=new Sprite();
menu.menuBackGroundImage=new Image{sprite=shotSprite,color=new Color(1f,1f,1f,1f)};menu.menuOverlayBG=new Image{sprite=nativeArt,color=new Color(0.7f,0.5f,0.3f,0.2f)};menu.menuOverlayBGKeret=new Image{sprite=keretArt,color=new Color(1f,1f,1f,1f)};
menuhandler.s=menu;menu.GLoadText.SetActive(true);DataFromMenuScript.s.mainmenu=true;Tick(8,true);
Check(menu.GLoadText.activeSelf,"preserve native transition overlay");Check(!pbg.activeSelf && pbg.image.sprite==null && pbg.image.color.a==0.588f,"menu transition restored the dimmer");Check(pBase.gameObject.destroyed,"menu transition removed the layers");
UnityEngine.Object.Destroy(shotSprite);
DataFromMenuScript.s.mainmenu=false;var next=new menuhandler();menuhandler.s=next;next.menuBackGroundImage=new Image{sprite=null,color=new Color(0.9056604f,0.8620289f,0.6792453f,1f)};var otherArt=new Sprite();next.menuOverlayBG=new Image{sprite=otherArt,color=new Color(0.9f,0.8f,0.7f,0.4f)};var otherKeret=new Sprite();next.menuOverlayBGKeret=new Image{sprite=otherKeret,color=new Color(1f,1f,1f,1f)};
var bg2Art=new Sprite();var bg2=new GameObject();bg2.layer=28;bg2.image=new Image{sprite=bg2Art,color=new Color(0.2f,0.3f,0.4f,0.6f)};next.GLoadText.transform.findResult=bg2.transform;bg2.transform.SetParent(next.GLoadText.transform,false);Adapter.Ground(false);
Tick(10,true);Check(next.GLoadText.activeSelf,"next scene overlay held");Check(bg2.activeSelf && bg2.image.sprite==bg2Art && bg2.image.color.a==0.6f,"dimmer keeps its own art under the layers");
var nBase=Kid(next.GLoadText.transform,"TldMpLoadingBase");var nPaper=Kid(next.GLoadText.transform,"TldMpLoadingPaper");var nFrame=Kid(next.GLoadText.transform,"TldMpLoadingFrame");
Check(nBase!=null && nPaper!=null && nFrame!=null,"captured stack rebuilt in the new scene");
var nBaseImg=nBase.gameObject.GetComponent<Image>();var nPaperImg=nPaper.gameObject.GetComponent<Image>();var nFrameImg=nFrame.gameObject.GetComponent<Image>();
Check(nBaseImg.sprite!=shotSprite && nBaseImg.sprite!=null && nBaseImg.sprite.texture==texB,"base shows the owned screenshot clone after the original was destroyed");
Check(nBaseImg.color.a==1f,"base stays opaque");
Check(nPaperImg.sprite==nativeArt && nPaperImg.color.a==1f && nPaperImg.color.r==0.7f,"held overlay reuses the art captured on the menu at full alpha");
Check(nFrameImg.sprite==keretArt && nFrameImg.color.a==1f,"captured border shown");
Check(menu.menuOverlayBG.color.a==0.2f,"captured paper source keeps its original tint");
float composite=1f-(1f-nBaseImg.color.a)*(1f-0.2f*nPaperImg.color.a);Check(composite==1f,"intrinsically translucent paper over an opaque base still covers");
Check(nBase.GetSiblingIndex()<nPaper.GetSiblingIndex() && nPaper.GetSiblingIndex()<nFrame.GetSiblingIndex() && nFrame.GetSiblingIndex()<bg2.transform.GetSiblingIndex(),"layers sit in order under the dimmer");
DataFromMenuScript.s.mainmenu=true;Tick(11,true);Check(next.GLoadText.activeSelf,"native transition overlay kept for menu return");Check(!bg2.activeSelf && bg2.image.sprite==bg2Art && bg2.image.color.a==0.6f,"held dimmer restored for the menu transition");Check(nBase.gameObject.destroyed,"layers removed on menu transition");Check(nBaseImg.sprite.destroyed,"owned screenshot clone released when the source went null");
DataFromMenuScript.s.mainmenu=false;Tick(12,true);
var nPaper2=Kid(next.GLoadText.transform,"TldMpLoadingPaper");Check(nPaper2!=null && nPaper2.gameObject.GetComponent<Image>().sprite==otherArt,"held overlay follows the menu's latest pick");
var nBase2=Kid(next.GLoadText.transform,"TldMpLoadingBase");var nBase2Img=nBase2.gameObject.GetComponent<Image>();Check(nBase2Img.sprite==null && nBase2Img.color.r==0.9056604f && nBase2Img.color.b==0.6792453f && nBase2Img.color.a==1f,"base falls back to the warm colour, not a stale screenshot");Check(nBase2.gameObject.layer==28,"rebuilt layer keeps the panel's UI layer");
var swap=new menuhandler();menuhandler.s=swap;var swapBgArt=new Sprite();var swapBg=new GameObject();swapBg.layer=28;swapBg.image=new Image{sprite=swapBgArt,color=new Color(0.1f,0.1f,0.1f,0.7f)};swap.GLoadText.transform.findResult=swapBg.transform;swapBg.transform.SetParent(swap.GLoadText.transform,false);
Tick(13,true);Check(swap.GLoadText.activeSelf,"scene switch holds the new overlay");Check(!bg2.activeSelf && bg2.image.sprite==bg2Art && bg2.image.color.r==0.2f,"scene switch restored the previous dimmer");Check(nBase2.gameObject.destroyed,"scene switch removed the old layers");
var sBase=Kid(swap.GLoadText.transform,"TldMpLoadingBase");Check(sBase!=null,"new scene layers built");Check(sBase.GetSiblingIndex()<swapBg.transform.GetSiblingIndex(),"layers under the new dimmer");
Tick(43.2f,true);Check(!swap.GLoadText.activeSelf,"timeout releases");Check(!swapBg.activeSelf && swapBg.image.sprite==swapBgArt && swapBg.image.color.a==0.7f,"timeout restores the dimmer");Check(sBase.gameObject.destroyed,"timeout removes the layers");
var third=new menuhandler();menuhandler.s=third;var bg3Art=new Sprite();var bg3=new GameObject();bg3.layer=28;bg3.image=new Image{sprite=bg3Art,color=new Color(0.5f,0.6f,0.7f,0f)};third.GLoadText.transform.findResult=bg3.transform;bg3.transform.SetParent(third.GLoadText.transform,false);
Tick(50,true);Check(bg3.activeSelf,"error-path background shown");var tBase=Kid(third.GLoadText.transform,"TldMpLoadingBase");Check(tBase!=null,"error-path layers shown");Adapter.LoadingOverlayFailed(new Exception());Check(!third.GLoadText.activeSelf,"error releases");Check(!bg3.activeSelf && bg3.image.sprite==bg3Art && bg3.image.color.a==0f,"error restores the dimmer");Check(tBase.gameObject.destroyed,"error removes the layers");
var fourth=new menuhandler();menuhandler.s=fourth;Adapter.Ground(true);mainscript.s.player.transform.position=new Vector3{y=-10};Tick(60,true);Check(fourth.GLoadText.activeSelf,"hold while below ground");Check(Math.Abs(mainscript.s.player.transform.position.y-0.05f)<0.001,"player lifted onto ground");Tick(61,true);Tick(61.5f,true);Check(fourth.GLoadText.activeSelf,"wait for stability after lift");Tick(62.1f,true);Check(!fourth.GLoadText.activeSelf,"overlay hidden after lift and hold");
var fifth=new menuhandler();menuhandler.s=fifth;mainscript.s.player.transform.position=new Vector3{y=0};Adapter.Ground(true);Adapter.Support(false);Tick(70,true);Check(fifth.GLoadText.activeSelf,"wait for player support, not the terrain tile ray");Adapter.Support(true);Tick(71,true);Tick(71.5f,true);Check(fifth.GLoadText.activeSelf,"wait for stability after support");Tick(72.1f,true);Check(!fifth.GLoadText.activeSelf,"overlay hidden once the player is supported");
Console.WriteLine("PASS loading overlay readiness, native composition, multiplayer, lifecycle, fallback, lift and support");}}
'''.replace('METHOD', source[begin:end])
        compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='roadworthy-loading-') as directory:
            path = Path(directory)
            (path / 'Test.cs').write_text(fixture, encoding='utf-8')
            build = subprocess.run([str(compiler), '/nologo', '/out:' + str(path / 'Test.exe'), str(path / 'Test.cs')], capture_output=True, text=True)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            run = subprocess.run([str(path / 'Test.exe')], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
            self.assertIn('PASS loading overlay', run.stdout)

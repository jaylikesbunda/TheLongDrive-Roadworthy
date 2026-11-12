using System;
using UnityEngine;

namespace TldMp
{
    // Beer: a new liquid with numeric fluid id 8, appended after the game's
    // seven native fluids and Roadworthy's urine (id 7). Every native ordinal
    // stays intact. Native code treats it like any other non-fuel liquid; the
    // drinking effects below are applied by the bridge's two drink paths.
    //
    // Its display name lives in one extra slot appended to the game's word
    // table (ls.defww / ls.ww), which the game rebuilds on every scene and
    // language load, so EnsureWord re-adds it whenever the slot is missing.
    public static class BeerLiquid
    {
        public const int FluidId=8;
        internal const mainscript.fluidenum Fluid=(mainscript.fluidenum)FluidId;
        public const string Name="Beer";
        public static readonly Color Colour=new Color(0.86f,0.58f,0.12f,1f);

        // Per liter of beer drunk. Native "alcohol" is the game's spirit; beer
        // quenches thirst a little less than water and carries a fifth of the
        // spirit's healing, diuretic and drunk effect.
        public const float Hydration=0.9f;
        public const float AlcoholShare=0.2f;

        // Chance weight added to bottle random liquid lists: the same weight as
        // the bottle's native alcohol entry, so about as many bottles hold beer.
        public const float BottleChance=10f;

        static int wordIndex=-1;
        // ls.w has no beer word; the appended slot index is the enum's size.
        public static int WordIndex
        {
            get
            {
                if(wordIndex<0)wordIndex=Enum.GetNames(typeof(ls.w)).Length;
                return wordIndex;
            }
        }
        public static ls.w Word{get{return (ls.w)WordIndex;}}

        public static float Amount(mainscript.fluidcontainer container)
        {
            if(container==null || container.fluids==null)return 0f;
            float total=0f;
            for(int i=0;i<container.fluids.Count;i++)
            {
                var fluid=container.fluids[i];
                if(fluid!=null && fluid.type==Fluid)total+=fluid.amount;
            }
            return total;
        }

        // Pure: what drinking this many liters of beer does.
        public static void Effects(float liters,out float hydration,out float alcoholEquivalent)
        {
            if(!(liters>0f) || float.IsInfinity(liters)){hydration=0f;alcoholEquivalent=0f;return;}
            hydration=liters*Hydration;alcoholEquivalent=liters*AlcoholShare;
        }

        public static void ApplyDrink(survivalscript survival,float liters)
        {
            if(survival==null)return;
            float hydration,alcohol;Effects(liters,out hydration,out alcohol);
            if(!(hydration>0f))return;
            survival.Drink(hydration);
            survival.Heal(alcohol*survival.alcoholHeal);
            survival.ConvWaterToPiss(alcohol*survival.alcoholConvertToPiss);
            DrunkEffects.NoteAlcohol(survival,alcohol);
        }

        // Appends "Beer" to the word table and its defaults when the slot is
        // missing. Cheap when already present (two length checks).
        public static void EnsureWord()
        {
            var language=ls.l;if(language==null)return;
            int index=WordIndex;
            language.defww=WithWord(language.defww,index);
            language.ww=WithWord(language.ww,index);
        }
        internal static string[] WithWord(string[] table,int index)
        {
            if(table==null || table.Length!=index)return table;
            var grown=new string[index+1];Array.Copy(table,grown,index);grown[index]=Name;
            return grown;
        }

        // Adds beer to every random liquid list that already offers the game's
        // alcohol in a bottle-sized tank (the Beer bottle), once per prefab list.
        static bool bottlesDone;
        public static void EnsureBottles()
        {
            if(bottlesDone)return;
            var database=itemdatabase.s;if(database==null || database.items==null)return;
            bottlesDone=true;int changed=0;
            for(int i=0;i<database.items.Length;i++)
            {
                var item=database.items[i];
                if(item==null || item.name!="Beer")continue;
                foreach(var tank in item.GetComponentsInChildren<tankscript>(true))
                {
                    var grown=WithBeer(tank.randomFluids);
                    if(!ReferenceEquals(grown,tank.randomFluids)){tank.randomFluids=grown;changed++;}
                }
            }
            Debug.Log("[Roadworthy beer] bottle liquid lists with beer added: "+changed);
        }
        // Fixed house props bypass RandomFluid: their alcohol is serialized
        // directly on the bottle. Change fresh props before save/sync registration.
        internal static void PrepareFixedBottle(tosaveitemscript item)
        {
            if(item==null || item.id!=71 || item.started || item.loaded || !syncScript.IsHostOrSingle())return;
            foreach(var tank in item.GetComponentsInChildren<tankscript>(true))
                if(tank.F!=null && tank.F.maxC<=.501f && ReplaceBottledAlcohol(tank.F))tank.Upd(true);
        }

        internal static bool ReplaceBottledAlcohol(mainscript.fluidcontainer contents)
        {
            if(contents==null || contents.fluids==null)return false;
            bool alcohol=false;
            foreach(var fluid in contents.fluids)
            {
                if(fluid==null || !(fluid.amount>0f))continue;
                if(fluid.type!=mainscript.fluidenum.alcohol)return false;
                alcohol=true;
            }
            if(!alcohol)return false;
            foreach(var fluid in contents.fluids)
                if(fluid!=null && fluid.type==mainscript.fluidenum.alcohol)fluid.type=Fluid;
            return true;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<mainscript,object> barrelWorlds=new System.Runtime.CompilerServices.ConditionalWeakTable<mainscript,object>();

        internal static bool BarrelAllowed(bool newGame,bool host,bool fixedWereSpawned,bool startingHouse,bool alreadySpawned)
        {return newGame && host && !fixedWereSpawned && startingHouse && !alreadySpawned;}

        // Called after the native fixed props spawn, inside the existing POI
        // construction unit. Loaded POIs and guests never create another item.
        internal static void SpawnStartingBarrel(poiScript house,poiGenScript.poiClass poi,bool fixedWereSpawned)
        {
            var main=mainscript.s;var data=DataFromMenuScript.s;
            var map=menuhandler.s==null?null:menuhandler.s.currentMainMap;
            if(main==null || house==null || poi==null || map==null || map.poiGens==null)return;
            bool starting=false;
            foreach(var gen in map.poiGens)
                if(gen!=null && gen.index==house.poigenid && gen.placeStartHouse && gen.startHouseStarted && poi.id==(int)gen.startHouseType)starting=true;
            object marker;
            if(!BarrelAllowed(data!=null && data.newgame,syncScript.IsHostOrSingle(),fixedWereSpawned,starting,barrelWorlds.TryGetValue(main,out marker)))return;
            var db=itemdatabase.s;
            if(db==null || db.items==null || db.items.Length<=70 || db.items[70]==null)return;
            Physics.SyncTransforms();
            foreach(var tank in UnityEngine.Object.FindObjectsOfType<tankscript>())
            {
                if(tank==null || !tank.name.StartsWith("BARREL",StringComparison.Ordinal) || tank.F==null || tank.F.maxC!=80f || Vector3.Distance(tank.transform.position,house.transform.position)>18f)continue;
                Bounds bounds=new Bounds();bool found=false;
                foreach(var collider in tank.GetComponentsInChildren<Collider>())
                {
                    if(!collider.enabled || collider.isTrigger)continue;
                    if(!found){bounds=collider.bounds;found=true;}else bounds.Encapsulate(collider.bounds);
                }
                if(!found || bounds.size.y<.2f)continue;
                Vector3 position;
                if(!FindBarrelSpot(house.transform,bounds,out position))continue;
                // Match the anchor's shape and orientation used for clearance.
                var obj=UnityEngine.Object.Instantiate(db.items[70],position-(bounds.center-tank.transform.position),tank.transform.rotation);
                obj.transform.localScale=tank.transform.lossyScale;
                var save=obj.GetComponent<tosaveitemscript>();
                if(save==null){UnityEngine.Object.Destroy(obj);continue;}
                barrelWorlds.Add(main,new object());
                save.RandomSpawned(new newRandomStuffSpawnScript.item {
                    overrideFluidType=true,fluids=new[]{new tankscript.randomfluidclass{type=Fluid,chance=1f}},noMixedFluids=true,
                    overrideFluidAmount=true,minFluid=40f,maxFluid=40f });
                var rb=obj.GetComponent<Rigidbody>();if(rb!=null)rb.isKinematic=true;
                save.FStart();
                Debug.Log("[Roadworthy beer] starting barrel registered id="+save.idInSave+" local="+house.transform.InverseTransformPoint(obj.transform.position)+" liters="+Amount(obj.GetComponentInChildren<tankscript>().F));
                return;
            }
            Debug.LogWarning("[Roadworthy beer] no clear floor beside starting-house barrels.");
        }

        static bool FindBarrelSpot(Transform house,Bounds anchor,out Vector3 center)
        {
            var extents=anchor.extents;
            // Conservative world-aligned bounds include the same rotation/scale
            // as the existing barrel, with a small gap above the supporting floor.
            for(int ring=1;ring<=3;ring++)
                for(int direction=0;direction<8;direction++)
                {
                    float angle=direction*Mathf.PI*.25f;
                    var offset=house.TransformDirection(new Vector3(Mathf.Cos(angle),0f,Mathf.Sin(angle)))*(Mathf.Max(extents.x,extents.z)*2f+.15f)*ring;
                    var at=anchor.center+offset;
                    RaycastHit floor;
                    if(!Physics.Raycast(at+Vector3.up*1.5f,Vector3.down,out floor,anchor.size.y+3f,~0,QueryTriggerInteraction.Ignore))continue;
                    if(floor.normal.y<.95f || floor.collider.attachedRigidbody!=null || !floor.collider.transform.IsChildOf(house))continue;
                    if(Mathf.Abs(floor.point.y-anchor.min.y)>.35f)continue;
                    center=new Vector3(at.x,floor.point.y+extents.y+.05f,at.z);
                    if(!Physics.CheckBox(center,extents+new Vector3(.03f,0f,.03f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore))return true;
                }
            center=Vector3.zero;return false;
        }

        internal static tankscript.randomfluidclass[] WithBeer(tankscript.randomfluidclass[] list)
        {
            if(list==null)return list;
            bool hasAlcohol=false;
            for(int i=0;i<list.Length;i++)
            {
                if(list[i]==null)continue;
                if(list[i].type==Fluid)return list;
                if(list[i].type==mainscript.fluidenum.alcohol)hasAlcohol=true;
            }
            if(!hasAlcohol)return list;
            var grown=new tankscript.randomfluidclass[list.Length+1];Array.Copy(list,grown,list.Length);
            grown[list.Length]=new tankscript.randomfluidclass{type=Fluid,chance=BottleChance};
            return grown;
        }
    }
}

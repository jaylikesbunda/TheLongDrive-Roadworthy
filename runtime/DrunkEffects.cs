using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TldMp
{
    // Local-only drunk level. Alcohol and milk intake are read at the two drink
    // paths that already run the native alcohol heal (StableGameBridge.DrinkTransfer
    // and ParticleFluidDrinking.Transfer), so nothing here adds an IL hook. Nothing
    // is sent over the network, and no input, steering or car handling changes.
    public static class DrunkEffects
    {
        sealed class State {internal float Level;}
        static readonly ConditionalWeakTable<survivalscript,State> levels=new ConditionalWeakTable<survivalscript,State>();

        static bool IsLocal(survivalscript survival)
        {
            var main=mainscript.s;
            return survival!=null && main!=null && main.player!=null && main.player.survival==survival;
        }

        // Same amount the native heal uses: poured volume times the source's alcohol share.
        public static float AlcoholDrunk(mainscript.fluidcontainer source,float amount)
        {
            if(source==null || !(amount>0f))return 0f;
            return amount*source.GetPercent(mainscript.fluidenum.alcohol);
        }

        // Liters of milk in a pour, computed the same way as AlcoholDrunk.
        public static float MilkDrunk(mainscript.fluidcontainer source,float amount)
        {
            if(source==null || !(amount>0f))return 0f;
            return amount*source.GetPercent(mainscript.fluidenum.milk);
        }

        public static void NoteAlcohol(survivalscript survival,float liters)
        {
            if(!(liters>0f) || !IsLocal(survival))return;
            var state=levels.GetOrCreateValue(survival);
            state.Level=DrunkPolicy.Add(state.Level,liters);
        }

        // Milk sobers the local player up. No-op when there is no level state yet.
        public static void NoteMilk(survivalscript survival,float liters)
        {
            State state;
            if(!(liters>0f) || !IsLocal(survival) || !levels.TryGetValue(survival,out state))return;
            state.Level=DrunkPolicy.Sober(state.Level,liters);
        }

        public static float Level(survivalscript survival)
        {
            State state;
            return survival!=null && levels.TryGetValue(survival,out state) ? state.Level : 0f;
        }

        public static void Tick(survivalscript survival,float seconds)
        {
            State state;
            if(survival==null || !levels.TryGetValue(survival,out state) || !(state.Level>0f))return;
            state.Level=DrunkPolicy.Decay(state.Level,seconds);
        }

        public static void Clear(survivalscript survival)
        {
            if(survival!=null)levels.Remove(survival);
        }
    }

    // Sways the local player's camera a little while drunk, and breathes its field
    // of view. Runs in Camera.onPreCull and undoes both in Camera.onPostRender, so
    // the game's own Update and LateUpdate (mouse look, aim, interaction rays) only
    // ever see the real rotation and field of view. Only the player's camera is
    // touched: the car mirrors render their own cameras and are ignored.
    // Nothing is applied while the cursor is free (menus, pause) or when the
    // Drunk effects option is off.
    public sealed class DrunkCameraSway : MonoBehaviour
    {
        Camera restoreCamera;
        Quaternion restoreRotation;
        float restoreFov;
        bool pending;
        float elapsed;

        void OnEnable()
        {
            Camera.onPreCull += PreCull;
            Camera.onPostRender += PostRender;
        }

        void OnDisable()
        {
            Camera.onPreCull -= PreCull;
            Camera.onPostRender -= PostRender;
            Restore();
        }

        void Update()
        {
            var main=mainscript.s;
            if(main==null || main.player==null){elapsed+=Time.deltaTime;return;}
            float level=DrunkEffects.Level(main.player.survival);
            elapsed+=Time.deltaTime*DrunkPolicy.SwaySpeed(level);
            DrunkEffects.Tick(main.player.survival,Time.deltaTime);
        }

        void PreCull(Camera cam)
        {
            var main=mainscript.s;
            // Mirror and other cameras are left alone entirely.
            if(cam==null || main==null || main.player==null || main.player.Cam!=cam)return;
            Restore();
            if(!PatchOptions.DrunkEffects || !main.crsrLocked)return;
            float level=DrunkEffects.Level(main.player.survival);
            if(!(level>0f))return;
            float pitch,yaw,roll;
            DrunkPolicy.Angles(level,elapsed,out pitch,out yaw,out roll);
            Transform t=cam.transform;
            restoreCamera=cam;
            restoreRotation=t.localRotation;
            restoreFov=cam.fieldOfView;
            pending=true;
            t.localRotation=restoreRotation*Quaternion.Euler(pitch,yaw,roll);
            cam.fieldOfView=restoreFov+DrunkPolicy.FovOffset(level,elapsed);
        }

        void PostRender(Camera cam)
        {
            if(pending && cam==restoreCamera)Restore();
        }

        void Restore()
        {
            if(pending && restoreCamera!=null)
            {
                restoreCamera.transform.localRotation=restoreRotation;
                restoreCamera.fieldOfView=restoreFov;
            }
            pending=false;
            restoreCamera=null;
        }
    }
}

using System.Collections;
using UnityEngine;

namespace TldMp
{
    public static class RabbitAi
    {
        public static float HearingFalloff(float from,float to,float squaredDistance,sensoryscript senses)
        {
            if(senses!=null && senses.GetComponent<nyulscript>()!=null)return Mathf.InverseLerp(to,from,squaredDistance);
            return Mathf.InverseLerp(from,to,squaredDistance);
        }
        public static Coroutine StartFlee(MonoBehaviour owner,IEnumerator routine)
        {
            var ai=owner as aiscript;
            if(ai==null || ai.nyul==null)return owner.StartCoroutine(routine);
            if(!ai.alive || !ai.isActiveAndEnabled){ai.fleeing=false;Discard(routine);return null;}
            var state=ai.GetComponent<RabbitFleeState>();
            if(state==null)state=ai.gameObject.AddComponent<RabbitFleeState>();
            if(state.running){Discard(routine);return state.handle;}
            state.owner=ai;
            state.running=true;
            var started=state.StartCoroutine(state.Run(routine));
            if(state.running)state.handle=started;
            return state.handle;
        }
        internal static void Discard(IEnumerator routine)
        {
            var disposable=routine as System.IDisposable;
            if(disposable!=null)disposable.Dispose();
        }
        internal static void CancelFlee(aiscript ai)
        {
            if(ai==null)return;
            var state=ai.GetComponent<RabbitFleeState>();
            if(state==null||!state.running)return;
            state.StopAllCoroutines();
            state.running=false;state.handle=null;
            ai.fleeing=false;
        }
    }

    public sealed class RabbitFleeState:MonoBehaviour
    {
        internal aiscript owner;
        internal Coroutine handle;
        internal bool running;
        internal IEnumerator Run(IEnumerator routine)
        {
            try
            {
                while(owner!=null && owner.alive && owner.isActiveAndEnabled && routine.MoveNext())
                    yield return routine.Current;
            }
            finally
            {
                running=false;
                handle=null;
                RabbitAi.Discard(routine);
                if(owner!=null && (!owner.alive || !owner.isActiveAndEnabled))owner.fleeing=false;
            }
        }
        internal void OnDisable()
        {
            StopAllCoroutines();
            running=false;
            handle=null;
            if(owner!=null)owner.fleeing=false;
        }
    }
}

using System;
namespace TldMp
{
    internal static class BusRearAuthority
    {
        // Native ragdoll packets also carry the articulated rear carriage. A
        // local driver must not accept a delayed remote pose for that body.
        internal static bool Allow(byte[] bytes,bool connected,savedatascript data)
        {
            if(!connected||bytes==null||bytes.Length<5||bytes[0]!=43||data==null||data.items==null)return true;
            uint id=BitConverter.ToUInt32(bytes,1);tosaveitemscript item;
            if(!data.items.TryGetValue(id,out item)||item==null||item.idInSave!=id||item.car==null||item.GetComponent<BusArticulation>()==null)return true;
            // While the paired replica owns both bodies the rear timeline comes
            // only from the pair stream; a delayed ragdoll packet must not snap it.
            if(BusPairReplica.Owns(item))return false;
            return item.CarWhatDoing()!=tosaveitemscript.carWhatDoing.driving;
        }
    }
}

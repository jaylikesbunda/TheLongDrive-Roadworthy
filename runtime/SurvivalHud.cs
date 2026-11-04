using UnityEngine;
using UnityEngine.UI;

namespace TldMp
{
    public static class SurvivalHud
    {
        static Image tinted;
        static Color original;

        public static void Begin()
        {
            var main=mainscript.s;
            if(main!=null && main.player!=null && SurvivalRates.IsSick(main.player.survival))main.player.ShowStats();
        }
        static void Restore()
        {
            if(tinted!=null){var c=tinted.color;tinted.color=new Color(original.r,original.g,original.b,c.a);}
            tinted=null;
        }
        public static void End()
        {
            var main=mainscript.s;
            var image=main!=null && main.menu!=null ? main.menu.survivalSliderWater : null;
            bool sick=main!=null && main.player!=null && SurvivalRates.IsSick(main.player.survival);
            if(!sick || image==null){Restore();return;}
            if(tinted!=image){Restore();tinted=image;original=image.color;}
            var color=image.color;
            float pulse=(float)(0.5+0.5*System.Math.Cos(Time.unscaledTime*2.0*System.Math.PI/3.0));
            image.color=new Color(original.r+(1f-original.r)*pulse,original.g+(0.85f-original.g)*pulse,original.b+(0.1f-original.b)*pulse,color.a);
        }
    }
}

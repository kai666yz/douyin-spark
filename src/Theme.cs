using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace DouyinSpark
{
    static class KittyTheme
    {
        public static readonly Color KPink = Color.FromArgb(164, 94, 118);      // 主粉
        public static readonly Color KPinkDeep = Color.FromArgb(144, 78, 101);   // 深粉
        public static readonly Color KPinkSoft = Color.FromArgb(249, 244, 244);  // 浅粉
        public static readonly Color KPinkBg = Color.FromArgb(255, 247, 250);    // 淡粉背景
        public static readonly Color KPinkBox = Color.FromArgb(255, 255, 255);   // 卡片底
        public static readonly Color KPinkLine = Color.FromArgb(183, 126, 145);  // 描边粉
        public static readonly Color KPinkText = Color.FromArgb(58, 46, 51);    // 粉灰文字

        // 自绘 HelloKitty 风格猫脸（白猫脸 + 蝴蝶结 + 胡须），cx/cy 中心，s 缩放
        public static void PaintKittyFace(Graphics g, float cx, float cy, float s)
        {
            using (var ear = new SolidBrush(Color.White))
            using (var pen = new Pen(KPinkLine, Math.Max(1.5f, s * 0.045f)))
            {
                g.FillPolygon(ear, new PointF[] { new PointF(cx - 0.40f * s, cy - 0.16f * s), new PointF(cx - 0.52f * s, cy - 0.44f * s), new PointF(cx - 0.16f * s, cy - 0.34f * s) });
                g.FillPolygon(ear, new PointF[] { new PointF(cx + 0.40f * s, cy - 0.16f * s), new PointF(cx + 0.52f * s, cy - 0.44f * s), new PointF(cx + 0.16f * s, cy - 0.34f * s) });
                using (var face = new SolidBrush(Color.White))
                    g.FillEllipse(face, cx - 0.50f * s, cy - 0.32f * s, s, 0.82f * s);
                g.DrawEllipse(pen, cx - 0.50f * s, cy - 0.32f * s, s, 0.82f * s);
            }
            using (var eye = new SolidBrush(Color.FromArgb(40, 35, 45)))
            {
                g.FillEllipse(eye, cx - 0.26f * s, cy - 0.08f * s, 0.13f * s, 0.17f * s);
                g.FillEllipse(eye, cx + 0.13f * s, cy - 0.08f * s, 0.13f * s, 0.17f * s);
            }
            using (var nose = new SolidBrush(Color.FromArgb(250, 205, 90)))
                g.FillEllipse(nose, cx - 0.06f * s, cy + 0.06f * s, 0.13f * s, 0.11f * s);
            using (var whisker = new Pen(Color.FromArgb(175, 130, 150), Math.Max(1f, s * 0.03f)))
            {
                g.DrawLine(whisker, cx - 0.30f * s, cy - 0.02f * s, cx - 0.60f * s, cy - 0.11f * s);
                g.DrawLine(whisker, cx - 0.30f * s, cy + 0.07f * s, cx - 0.60f * s, cy + 0.03f * s);
                g.DrawLine(whisker, cx - 0.30f * s, cy + 0.16f * s, cx - 0.58f * s, cy + 0.16f * s);
                g.DrawLine(whisker, cx + 0.30f * s, cy - 0.02f * s, cx + 0.60f * s, cy - 0.11f * s);
                g.DrawLine(whisker, cx + 0.30f * s, cy + 0.07f * s, cx + 0.60f * s, cy + 0.03f * s);
                g.DrawLine(whisker, cx + 0.30f * s, cy + 0.16f * s, cx + 0.58f * s, cy + 0.16f * s);
            }
            using (var bow = new SolidBrush(Color.FromArgb(185, 109, 134)))
            {
                g.FillEllipse(bow, cx - 0.74f * s, cy - 0.44f * s, 0.24f * s, 0.17f * s);
                g.FillEllipse(bow, cx - 0.50f * s, cy - 0.50f * s, 0.24f * s, 0.17f * s);
                g.FillEllipse(bow, cx - 0.56f * s, cy - 0.43f * s, 0.15f * s, 0.15f * s);
            }
        }
    }

}

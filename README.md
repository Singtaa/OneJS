> Note that this branch (onejs-v2) is a work in progress and not yet ready for use. The core fundation is more or less all set. We just need to spend some more time to bring it to parity with OneJS V1.

OneJS V2 is a major upgrade. The most notable change is the switch from Jint to Puerts (V8). This change brings significant performance improvements, the most important of which is zero-allocation interop between JS and Unity (as can be seen from the demo below). Here's what works in this early preliminary version: 

https://github.com/DragonGround/ScriptLib/assets/666527/e2f13f44-0370-4c90-8df7-ce9078dd04f1

```tsx
import { emo } from "onejs-core/styled"
import { Color, Vector2 } from "UnityEngine"
import { MeshGenerationContext } from "UnityEngine/UIElements"

var div = document.createElement("div")
div.classname = emo`width: 100%; height: 100%;`

const NUM_BARS = 1000
const BAR_LINE_WIDTH = 1
div.ve.generateVisualContent = (ctx: MeshGenerationContext) => {
    const { width, height } = ctx.visualElement.contentRect;
    const barWidth = width / NUM_BARS;
    const halfBarWidth = barWidth / 2;
    const painter = ctx.painter2D;

    painter.lineWidth = BAR_LINE_WIDTH;
    for (let i = 0; i < NUM_BARS; i++) {
        const x = barWidth * i + halfBarWidth;
        painter.strokeColor = Color.HSVToRGB(i / NUM_BARS, 1, 1);
        painter.BeginPath();
        painter.MoveTo(new Vector2(x, height));
        painter.LineTo(new Vector2(x, height * Math.random()));
        painter.Stroke();
    }
}

document.body.appendChild(div)
setInterval(() => { div.ve.MarkDirtyRepaint() })
```


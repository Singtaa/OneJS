> Note that this branch (onejs-v2) is a work in progress and not yet ready for use. The core foundation is more or less all set. We need to spend more time to bring it to parity with OneJS V1. If you are going to play with this branch right now, please do it in a brand new Unity project. Do not use it in your existing OneJS V1 projects yet.

OneJS V2 represents a substantial advancement, marking a pivotal shift from Jint to Puerts (V8). This transition results in considerable performance enhancements, chief among them being zero-allocation 😱🤯🎉 interop between JS and Unity (as can be seen from the demo below). Here's what works in this early preliminary version: 

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

> TODO need to cover many of the other new features and improvements in V2, such as the new esbuild workflow, automatic ts typings, onejs npm modules etc.
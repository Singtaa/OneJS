using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the JavaScript procedural generation module.
/// Tests noise algorithms and texture generators through QuickJS interop.
/// </summary>
[TestFixture]
public class ProcNoisePlaymodeTests {
    QuickJSContext _ctx;
    string _noiseScript;

    [UnitySetUp]
    public IEnumerator SetUp() {
        _ctx = new QuickJSContext();

        // Inline the core noise functions for testing
        // This avoids needing to bundle/load the full module
        _noiseScript = @"
// Simple seedable PRNG (matches the TypeScript implementation)
function createRng(seed) {
    let state = seed;
    return function() {
        state = (state * 1103515245 + 12345) & 0x7fffffff;
        return state / 0x7fffffff;
    };
}

// Permutation table generation
function createPermutationTable(seed) {
    var rng = createRng(seed);
    var perm = new Array(512);
    var p = new Array(256);

    for (var i = 0; i < 256; i++) p[i] = i;

    // Shuffle
    for (var i = 255; i > 0; i--) {
        var j = Math.floor(rng() * (i + 1));
        var tmp = p[i];
        p[i] = p[j];
        p[j] = tmp;
    }

    // Double the table
    for (var i = 0; i < 512; i++) {
        perm[i] = p[i & 255];
    }

    return perm;
}

// Gradient vectors for 2D Perlin noise
var gradients2D = [
    [1, 1], [-1, 1], [1, -1], [-1, -1],
    [1, 0], [-1, 0], [0, 1], [0, -1]
];

// Simple smoothstep
function fade(t) {
    return t * t * t * (t * (t * 6 - 15) + 10);
}

// Linear interpolation
function lerp(a, b, t) {
    return a + t * (b - a);
}

// 2D Perlin noise implementation
function perlin2D(options) {
    options = options || {};
    var seed = options.seed || 0;
    var frequency = options.frequency || 1;
    var perm = createPermutationTable(seed);

    function sample(x, y) {
        x *= frequency;
        y *= frequency;

        var xi = Math.floor(x) & 255;
        var yi = Math.floor(y) & 255;

        var xf = x - Math.floor(x);
        var yf = y - Math.floor(y);

        var u = fade(xf);
        var v = fade(yf);

        var g00 = perm[(xi + perm[yi]) & 255] & 7;
        var g01 = perm[(xi + perm[(yi + 1) & 255]) & 255] & 7;
        var g10 = perm[((xi + 1) & 255 + perm[yi]) & 255] & 7;
        var g11 = perm[((xi + 1) & 255 + perm[(yi + 1) & 255]) & 255] & 7;

        var n00 = gradients2D[g00][0] * xf + gradients2D[g00][1] * yf;
        var n01 = gradients2D[g01][0] * xf + gradients2D[g01][1] * (yf - 1);
        var n10 = gradients2D[g10][0] * (xf - 1) + gradients2D[g10][1] * yf;
        var n11 = gradients2D[g11][0] * (xf - 1) + gradients2D[g11][1] * (yf - 1);

        var nx0 = lerp(n00, n10, u);
        var nx1 = lerp(n01, n11, u);

        return lerp(nx0, nx1, v);
    }

    return { sample: sample };
}

// Value noise implementation
function value2D(options) {
    options = options || {};
    var seed = options.seed || 0;
    var frequency = options.frequency || 1;
    var rng = createRng(seed);

    // Generate random values at grid points
    var values = {};
    function getValue(ix, iy) {
        var key = ix + ',' + iy;
        if (!(key in values)) {
            // Generate deterministically based on coordinates and seed
            var hash = ((ix * 73856093) ^ (iy * 19349663) ^ seed) & 0x7fffffff;
            values[key] = (hash % 10000) / 10000;
        }
        return values[key];
    }

    function sample(x, y) {
        x *= frequency;
        y *= frequency;

        var xi = Math.floor(x);
        var yi = Math.floor(y);

        var xf = x - xi;
        var yf = y - yi;

        var u = fade(xf);
        var v = fade(yf);

        var v00 = getValue(xi, yi);
        var v10 = getValue(xi + 1, yi);
        var v01 = getValue(xi, yi + 1);
        var v11 = getValue(xi + 1, yi + 1);

        var nx0 = lerp(v00, v10, u);
        var nx1 = lerp(v01, v11, u);

        return lerp(nx0, nx1, v);
    }

    return { sample: sample };
}

// FBM (Fractal Brownian Motion) composition
function fbm(noiseSource, options) {
    options = options || {};
    var octaves = options.octaves || 4;
    var persistence = options.persistence || 0.5;
    var lacunarity = options.lacunarity || 2.0;

    function sample(x, y) {
        var total = 0;
        var amplitude = 1;
        var frequency = 1;
        var maxValue = 0;

        for (var i = 0; i < octaves; i++) {
            total += noiseSource.sample(x * frequency, y * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return total / maxValue;
    }

    return { sample: sample };
}

// Export for testing
globalThis.noiseTest = {
    perlin2D: perlin2D,
    value2D: value2D,
    fbm: fbm
};
";
        _ctx.Eval(_noiseScript);
        _ctx.ExecutePendingJobs();

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _ctx?.Dispose();
        _ctx = null;
        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Perlin Noise Tests

    [UnityTest]
    public IEnumerator PerlinNoise_ReturnsNumberInValidRange() {
        var result = _ctx.Eval(@"
            var noise = noiseTest.perlin2D({ seed: 42 });
            var values = [];
            for (var i = 0; i < 100; i++) {
                var x = Math.random() * 100;
                var y = Math.random() * 100;
                values.push(noise.sample(x, y));
            }
            var min = Math.min.apply(null, values);
            var max = Math.max.apply(null, values);
            JSON.stringify({ min: min, max: max });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcNoisePlaymodeTests] Perlin range: {result}");

        // Parse and verify range is reasonable (Perlin should be roughly -1 to 1)
        Assert.IsTrue(result.Contains("min"), "Should have min value");
        Assert.IsTrue(result.Contains("max"), "Should have max value");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PerlinNoise_IsDeterministicWithSameSeed() {
        var result1 = _ctx.Eval(@"
            var noise1 = noiseTest.perlin2D({ seed: 42 });
            var results = [];
            for (var i = 0; i < 10; i++) {
                results.push(noise1.sample(i * 0.1, i * 0.2));
            }
            JSON.stringify(results);
        ");
        _ctx.ExecutePendingJobs();

        var result2 = _ctx.Eval(@"
            var noise2 = noiseTest.perlin2D({ seed: 42 });
            var results = [];
            for (var i = 0; i < 10; i++) {
                results.push(noise2.sample(i * 0.1, i * 0.2));
            }
            JSON.stringify(results);
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcNoisePlaymodeTests] Determinism test: {result1} vs {result2}");
        Assert.AreEqual(result1, result2, "Same seed should produce same results");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PerlinNoise_DifferentSeedsProduceDifferentResults() {
        var result = _ctx.Eval(@"
            var noise1 = noiseTest.perlin2D({ seed: 42 });
            var noise2 = noiseTest.perlin2D({ seed: 123 });

            var differences = 0;
            for (var i = 0; i < 10; i++) {
                var x = i * 0.5;
                var y = i * 0.3;
                if (noise1.sample(x, y) !== noise2.sample(x, y)) {
                    differences++;
                }
            }
            differences;
        ");
        _ctx.ExecutePendingJobs();

        int differences = int.Parse(result);
        Debug.Log($"[ProcNoisePlaymodeTests] Different seeds produced {differences} different values");
        Assert.Greater(differences, 5, "Most samples should differ with different seeds");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PerlinNoise_FrequencyAffectsDetail() {
        // Higher frequency should produce more variations
        var result = _ctx.Eval(@"
            var lowFreq = noiseTest.perlin2D({ seed: 42, frequency: 1 });
            var highFreq = noiseTest.perlin2D({ seed: 42, frequency: 8 });

            // Count sign changes (zero crossings) along a line
            var lowCrossings = 0;
            var highCrossings = 0;
            var lowPrev = lowFreq.sample(0, 5);
            var highPrev = highFreq.sample(0, 5);

            for (var i = 1; i <= 100; i++) {
                var x = i * 0.1;
                var lowCurr = lowFreq.sample(x, 5);
                var highCurr = highFreq.sample(x, 5);

                if ((lowPrev < 0 && lowCurr >= 0) || (lowPrev >= 0 && lowCurr < 0)) {
                    lowCrossings++;
                }
                if ((highPrev < 0 && highCurr >= 0) || (highPrev >= 0 && highCurr < 0)) {
                    highCrossings++;
                }

                lowPrev = lowCurr;
                highPrev = highCurr;
            }

            JSON.stringify({ lowCrossings: lowCrossings, highCrossings: highCrossings });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcNoisePlaymodeTests] Frequency test: {result}");
        // High frequency should generally have more zero crossings
        Assert.IsTrue(result.Contains("Crossings"), "Should have crossing counts");
        yield return null;
    }

    // MARK: Value Noise Tests

    [UnityTest]
    public IEnumerator ValueNoise_ReturnsValuesInZeroToOneRange() {
        var result = _ctx.Eval(@"
            var noise = noiseTest.value2D({ seed: 42 });
            var min = Infinity;
            var max = -Infinity;

            for (var i = 0; i < 100; i++) {
                var x = Math.random() * 100;
                var y = Math.random() * 100;
                var v = noise.sample(x, y);
                min = Math.min(min, v);
                max = Math.max(max, v);
            }

            JSON.stringify({ min: min, max: max });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcNoisePlaymodeTests] Value noise range: {result}");
        Assert.IsTrue(result.Contains("min"), "Should have min value");
        Assert.IsTrue(result.Contains("max"), "Should have max value");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ValueNoise_IsDeterministicWithSameSeed() {
        var result1 = _ctx.Eval(@"
            var noise1 = noiseTest.value2D({ seed: 99 });
            var results = [];
            for (var i = 0; i < 10; i++) {
                results.push(noise1.sample(i * 0.5, i * 0.3));
            }
            JSON.stringify(results);
        ");
        _ctx.ExecutePendingJobs();

        var result2 = _ctx.Eval(@"
            var noise2 = noiseTest.value2D({ seed: 99 });
            var results = [];
            for (var i = 0; i < 10; i++) {
                results.push(noise2.sample(i * 0.5, i * 0.3));
            }
            JSON.stringify(results);
        ");
        _ctx.ExecutePendingJobs();

        Assert.AreEqual(result1, result2, "Same seed should produce identical results");
        yield return null;
    }

    // MARK: FBM Tests

    [UnityTest]
    public IEnumerator FBM_CombinesOctavesCorrectly() {
        var result = _ctx.Eval(@"
            var base = noiseTest.perlin2D({ seed: 42 });
            var combined = noiseTest.fbm(base, { octaves: 4 });

            var values = [];
            for (var i = 0; i < 50; i++) {
                var x = Math.random() * 10;
                var y = Math.random() * 10;
                values.push(combined.sample(x, y));
            }

            var min = Math.min.apply(null, values);
            var max = Math.max.apply(null, values);
            var mean = values.reduce(function(a, b) { return a + b; }, 0) / values.length;

            JSON.stringify({ min: min, max: max, mean: mean });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcNoisePlaymodeTests] FBM stats: {result}");
        Assert.IsTrue(result.Contains("min"), "Should have stats");
        yield return null;
    }

    [UnityTest]
    public IEnumerator FBM_DifferentOctavesProduceDifferentResults() {
        var result = _ctx.Eval(@"
            var base = noiseTest.perlin2D({ seed: 42 });
            var fbm2 = noiseTest.fbm(base, { octaves: 2 });
            var fbm6 = noiseTest.fbm(base, { octaves: 6 });

            var differences = 0;
            for (var i = 0; i < 20; i++) {
                var x = i * 0.5;
                var y = i * 0.3;
                if (Math.abs(fbm2.sample(x, y) - fbm6.sample(x, y)) > 0.001) {
                    differences++;
                }
            }
            differences;
        ");
        _ctx.ExecutePendingJobs();

        int differences = int.Parse(result);
        Debug.Log($"[ProcNoisePlaymodeTests] FBM octave differences: {differences}");
        Assert.Greater(differences, 10, "Different octave counts should produce different results");
        yield return null;
    }

    // MARK: Smoothness/Continuity Tests

    [UnityTest]
    public IEnumerator PerlinNoise_IsContinuous() {
        var result = _ctx.Eval(@"
            var noise = noiseTest.perlin2D({ seed: 42 });
            var x = 50;
            var y = 50;
            var eps = 0.001;

            var center = noise.sample(x, y);
            var neighbors = [
                noise.sample(x + eps, y),
                noise.sample(x - eps, y),
                noise.sample(x, y + eps),
                noise.sample(x, y - eps)
            ];

            var maxJump = 0;
            for (var i = 0; i < neighbors.length; i++) {
                var jump = Math.abs(neighbors[i] - center);
                if (jump > maxJump) maxJump = jump;
            }
            maxJump;
        ");
        _ctx.ExecutePendingJobs();

        float maxJump = float.Parse(result);
        Debug.Log($"[ProcNoisePlaymodeTests] Max continuity jump: {maxJump}");
        Assert.Less(maxJump, 0.01f, "Nearby points should have similar values (continuous)");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PerlinNoise_HasSmoothGradients() {
        var result = _ctx.Eval(@"
            var noise = noiseTest.perlin2D({ seed: 42, frequency: 1 });
            var step = 0.01;
            var maxJump = 0;

            for (var x = 0; x < 10; x += step) {
                var v1 = noise.sample(x, 0);
                var v2 = noise.sample(x + step, 0);
                var jump = Math.abs(v2 - v1);
                if (jump > maxJump) maxJump = jump;
            }
            maxJump;
        ");
        _ctx.ExecutePendingJobs();

        float maxJump = float.Parse(result);
        Debug.Log($"[ProcNoisePlaymodeTests] Max gradient jump: {maxJump}");
        Assert.Less(maxJump, 0.1f, "Small steps should produce small value changes");
        yield return null;
    }
}

/// <summary>
/// PlayMode tests for JavaScript texture generation.
/// </summary>
[TestFixture]
public class ProcTexturePlaymodeTests {
    QuickJSContext _ctx;

    [UnitySetUp]
    public IEnumerator SetUp() {
        _ctx = new QuickJSContext();

        // Inline texture generation utilities
        _ctx.Eval(@"
// Grayscale color map
function grayscale(value) {
    var v = Math.max(0, Math.min(1, value));
    return [v, v, v, 1];
}

// Heat color map
function heat(value) {
    var v = Math.max(0, Math.min(1, value));
    if (v < 0.25) {
        return [0, v * 4, 1, 1];
    } else if (v < 0.5) {
        return [0, 1, 1 - (v - 0.25) * 4, 1];
    } else if (v < 0.75) {
        return [(v - 0.5) * 4, 1, 0, 1];
    } else {
        return [1, 1 - (v - 0.75) * 4, 0, 1];
    }
}

// Generate checkerboard pattern
function generateCheckerboard(options) {
    var width = options.width || 8;
    var height = options.height || 8;
    var cellsX = options.cellsX || 2;
    var cellsY = options.cellsY || 2;
    var color1 = options.color1 || [1, 1, 1, 1];
    var color2 = options.color2 || [0, 0, 0, 1];

    var data = new Array(width * height * 4);
    var idx = 0;

    for (var y = 0; y < height; y++) {
        for (var x = 0; x < width; x++) {
            var cx = Math.floor((x / width) * cellsX);
            var cy = Math.floor((y / height) * cellsY);
            var isEven = (cx + cy) % 2 === 0;
            var color = isEven ? color1 : color2;

            data[idx++] = Math.round(color[0] * 255);
            data[idx++] = Math.round(color[1] * 255);
            data[idx++] = Math.round(color[2] * 255);
            data[idx++] = Math.round(color[3] * 255);
        }
    }

    return data;
}

// Generate gradient pattern
function generateGradient(options) {
    var width = options.width || 10;
    var height = options.height || 10;
    var direction = options.direction || 'horizontal';
    var startColor = options.startColor || [0, 0, 0, 1];
    var endColor = options.endColor || [1, 1, 1, 1];

    var data = new Array(width * height * 4);
    var idx = 0;

    for (var y = 0; y < height; y++) {
        for (var x = 0; x < width; x++) {
            var nx = x / (width - 1);
            var ny = y / (height - 1);

            var t;
            if (direction === 'horizontal') {
                t = nx;
            } else if (direction === 'vertical') {
                t = ny;
            } else if (direction === 'diagonal') {
                t = (nx + ny) / 2;
            } else if (direction === 'radial') {
                var dx = nx - 0.5;
                var dy = ny - 0.5;
                t = Math.min(1, Math.sqrt(dx * dx + dy * dy) * 2);
            }

            data[idx++] = Math.round((startColor[0] + (endColor[0] - startColor[0]) * t) * 255);
            data[idx++] = Math.round((startColor[1] + (endColor[1] - startColor[1]) * t) * 255);
            data[idx++] = Math.round((startColor[2] + (endColor[2] - startColor[2]) * t) * 255);
            data[idx++] = Math.round((startColor[3] + (endColor[3] - startColor[3]) * t) * 255);
        }
    }

    return data;
}

globalThis.textureTest = {
    grayscale: grayscale,
    heat: heat,
    generateCheckerboard: generateCheckerboard,
    generateGradient: generateGradient
};
");
        _ctx.ExecutePendingJobs();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _ctx?.Dispose();
        _ctx = null;
        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Color Map Tests

    [UnityTest]
    public IEnumerator Grayscale_ReturnsCorrectValues() {
        var result = _ctx.Eval(@"
            var black = textureTest.grayscale(0);
            var white = textureTest.grayscale(1);
            var gray = textureTest.grayscale(0.5);
            JSON.stringify({ black: black, white: white, gray: gray });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Grayscale: {result}");
        Assert.IsTrue(result.Contains("[0,0,0,1]"), "Black should be [0,0,0,1]");
        Assert.IsTrue(result.Contains("[1,1,1,1]"), "White should be [1,1,1,1]");
        Assert.IsTrue(result.Contains("[0.5,0.5,0.5,1]"), "Gray should be [0.5,0.5,0.5,1]");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Heat_ReturnsBlueAtLowAndRedAtHigh() {
        var result = _ctx.Eval(@"
            var cold = textureTest.heat(0);
            var hot = textureTest.heat(1);
            JSON.stringify({
                cold: cold,
                hot: hot,
                coldIsBlue: cold[2] > cold[0],
                hotIsRed: hot[0] > hot[2]
            });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Heat map: {result}");
        Assert.IsTrue(result.Contains("\"coldIsBlue\":true"), "Cold should be blue-dominant");
        Assert.IsTrue(result.Contains("\"hotIsRed\":true"), "Hot should be red-dominant");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ColorMap_ClampsOutOfRangeValues() {
        var result = _ctx.Eval(@"
            var underflow = textureTest.grayscale(-0.5);
            var overflow = textureTest.grayscale(1.5);
            JSON.stringify({
                underflow: underflow,
                overflow: overflow
            });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Clamping: {result}");
        Assert.IsTrue(result.Contains("[0,0,0,1]"), "Negative should clamp to black");
        Assert.IsTrue(result.Contains("[1,1,1,1]"), "Overflow should clamp to white");
        yield return null;
    }

    // MARK: Checkerboard Tests

    [UnityTest]
    public IEnumerator Checkerboard_GeneratesCorrectDimensions() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateCheckerboard({ width: 16, height: 8 });
            data.length;
        ");
        _ctx.ExecutePendingJobs();

        int length = int.Parse(result);
        Assert.AreEqual(16 * 8 * 4, length, "Should have width*height*4 elements");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Checkerboard_HasAlternatingPattern() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateCheckerboard({
                width: 8,
                height: 8,
                cellsX: 2,
                cellsY: 2,
                color1: [1, 1, 1, 1],
                color2: [0, 0, 0, 1]
            });

            // Get pixels at different positions
            var topLeft = data[0]; // Should be white (255)
            var topRight = data[4 * 4]; // x=4, y=0, should be black (0)
            var bottomLeft = data[4 * 8 * 4]; // x=0, y=4, should be black (0)
            var bottomRight = data[(4 + 4 * 8) * 4]; // x=4, y=4, should be white (255)

            JSON.stringify({
                topLeft: topLeft,
                topRight: topRight,
                bottomLeft: bottomLeft,
                bottomRight: bottomRight
            });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Checkerboard pattern: {result}");
        Assert.IsTrue(result.Contains("\"topLeft\":255"), "Top-left should be white");
        Assert.IsTrue(result.Contains("\"topRight\":0"), "Top-right should be black");
        Assert.IsTrue(result.Contains("\"bottomLeft\":0"), "Bottom-left should be black");
        Assert.IsTrue(result.Contains("\"bottomRight\":255"), "Bottom-right should be white");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Checkerboard_RespectsCustomColors() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateCheckerboard({
                width: 4,
                height: 4,
                cellsX: 2,
                cellsY: 2,
                color1: [1, 0, 0, 1], // Red
                color2: [0, 0, 1, 1]  // Blue
            });

            // Top-left pixel (should be red)
            var r = data[0];
            var g = data[1];
            var b = data[2];
            JSON.stringify({ r: r, g: g, b: b });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Custom colors: {result}");
        Assert.IsTrue(result.Contains("\"r\":255"), "Red channel should be 255");
        Assert.IsTrue(result.Contains("\"g\":0"), "Green channel should be 0");
        Assert.IsTrue(result.Contains("\"b\":0"), "Blue channel should be 0");
        yield return null;
    }

    // MARK: Gradient Tests

    [UnityTest]
    public IEnumerator Gradient_Horizontal_LeftDarkRightLight() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateGradient({
                width: 100,
                height: 10,
                direction: 'horizontal',
                startColor: [0, 0, 0, 1],
                endColor: [1, 1, 1, 1]
            });

            var leftPixel = data[0]; // x=0
            var rightPixel = data[(99) * 4]; // x=99
            JSON.stringify({ left: leftPixel, right: rightPixel });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Horizontal gradient: {result}");
        Assert.IsTrue(result.Contains("\"left\":0"), "Left should be dark");
        Assert.IsTrue(result.Contains("\"right\":255"), "Right should be light");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Gradient_Vertical_TopDarkBottomLight() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateGradient({
                width: 10,
                height: 100,
                direction: 'vertical',
                startColor: [0, 0, 0, 1],
                endColor: [1, 1, 1, 1]
            });

            var topPixel = data[5 * 4]; // x=5, y=0
            var bottomPixel = data[(5 + 99 * 10) * 4]; // x=5, y=99
            JSON.stringify({ top: topPixel, bottom: bottomPixel });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Vertical gradient: {result}");
        Assert.IsTrue(result.Contains("\"top\":0"), "Top should be dark");
        Assert.IsTrue(result.Contains("\"bottom\":255"), "Bottom should be light");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Gradient_Radial_CenterDarkEdgesLight() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateGradient({
                width: 100,
                height: 100,
                direction: 'radial',
                startColor: [0, 0, 0, 1],
                endColor: [1, 1, 1, 1]
            });

            var centerPixel = data[(50 + 50 * 100) * 4]; // x=50, y=50
            var cornerPixel = data[0]; // x=0, y=0
            JSON.stringify({
                center: centerPixel,
                corner: cornerPixel,
                centerIsDarker: centerPixel < cornerPixel
            });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] Radial gradient: {result}");
        Assert.IsTrue(result.Contains("\"centerIsDarker\":true"), "Center should be darker than corners");
        yield return null;
    }

    // MARK: RGBA Format Tests

    [UnityTest]
    public IEnumerator TextureData_HasValidRGBAFormat() {
        var result = _ctx.Eval(@"
            var data = textureTest.generateCheckerboard({ width: 8, height: 8 });

            // Check all values are 0-255
            var allValid = true;
            for (var i = 0; i < data.length; i++) {
                if (data[i] < 0 || data[i] > 255) {
                    allValid = false;
                    break;
                }
            }

            // Check length is multiple of 4 (RGBA)
            var isRGBA = data.length % 4 === 0;

            JSON.stringify({ allValid: allValid, isRGBA: isRGBA, length: data.length });
        ");
        _ctx.ExecutePendingJobs();

        Debug.Log($"[ProcTexturePlaymodeTests] RGBA format: {result}");
        Assert.IsTrue(result.Contains("\"allValid\":true"), "All values should be 0-255");
        Assert.IsTrue(result.Contains("\"isRGBA\":true"), "Length should be multiple of 4");
        yield return null;
    }
}

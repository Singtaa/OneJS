/**
 * Test fixture for custom element registration E2E tests.
 *
 * This file is built by esbuild into an IIFE bundle and loaded by
 * CustomElementPlaymodeTests.cs via Resources.Load<TextAsset>.
 *
 * Each __test_* function sets up a different test scenario.
 * The C# test calls the function, flushes React, then verifies
 * the resulting UI Toolkit hierarchy.
 */

import { registerElement, createComponent, render, View, Label } from "onejs-react"
import type { BaseProps, VisualElement } from "onejs-react"
import { useState, useEffect, useRef } from "react"

declare const CS: any
declare const __root: any
declare function useExtensions(typeRef: any): void

// Props for our test custom element (mirrors C# TestCustomProgressBar)
interface TestCustomProgressBarProps extends BaseProps {
    progress?: number
    trackColor?: string
}

// Register the custom C# element
registerElement("test-progress", CS.OneJS.Tests.TestCustomProgressBar)

// Create a typed React component
const TestProgress = createComponent<TestCustomProgressBarProps>("test-progress")

// ============================================================
// Test 1: Basic rendering with custom props
// ============================================================
;(globalThis as any).__test_basicRendering = () => {
    function App() {
        return (
            <View>
                <TestProgress
                    progress={0.75}
                    trackColor="#ff0000"
                    style={{ width: 200, height: 20 }}
                    className="custom-test"
                />
            </View>
        )
    }
    render(<App />, __root)
}

// ============================================================
// Test 2: Prop updates trigger element property changes
// ============================================================
;(globalThis as any).__test_propUpdates = () => {
    let setProgressFn: ((v: number) => void) | null = null

    function App() {
        const [progress, setProgress] = useState(0.25)
        setProgressFn = setProgress
        return (
            <View>
                <TestProgress progress={progress} trackColor="#00ff00" />
            </View>
        )
    }

    render(<App />, __root)

    ;(globalThis as any).__setProgress = (value: number) => {
        if (setProgressFn) setProgressFn(value)
    }
}

// ============================================================
// Test 3: Events work on custom elements
// ============================================================
;(globalThis as any).__test_events = () => {
    ;(globalThis as any).__clickCount = 0

    function App() {
        return (
            <View>
                <TestProgress
                    progress={0.5}
                    onClick={() => { (globalThis as any).__clickCount++ }}
                />
            </View>
        )
    }

    render(<App />, __root)
}

// ============================================================
// Test 4: Ref forwarding returns correct C# type
// ============================================================
;(globalThis as any).__test_refForwarding = () => {
    ;(globalThis as any).__refTypeName = null
    ;(globalThis as any).__refHandle = -1

    function App() {
        const ref = useRef<VisualElement>(null)

        useEffect(() => {
            if (ref.current) {
                ;(globalThis as any).__refTypeName = (ref.current as any).GetType().Name
                ;(globalThis as any).__refHandle = (ref.current as any).__csHandle
            }
        }, [])

        return (
            <View>
                <TestProgress ref={ref} progress={0.5} />
            </View>
        )
    }

    render(<App />, __root)
}

// ============================================================
// Test 5: Multiple custom elements in hierarchy
// ============================================================
;(globalThis as any).__test_multipleElements = () => {
    function App() {
        return (
            <View>
                <TestProgress progress={0.1} trackColor="#aaa" />
                <TestProgress progress={0.5} trackColor="#bbb" />
                <TestProgress progress={0.9} trackColor="#ccc" />
            </View>
        )
    }

    render(<App />, __root)
}

// ============================================================
// Test 6: Mixed built-in and custom elements
// ============================================================
;(globalThis as any).__test_mixedElements = () => {
    function App() {
        return (
            <View>
                <Label>Before</Label>
                <TestProgress progress={0.5} trackColor="#123" />
                <Label>After</Label>
            </View>
        )
    }

    render(<App />, __root)
}

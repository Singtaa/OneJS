import { useState } from "react"
import { render, View, Text, Label, Button, TextField, Toggle, Slider, ScrollView } from "onejs-react"

/**
 * OneJS Control Showcase
 * Demonstrates all supported UI controls with the runtime theme.
 */

function Section({ title, children }: { title: string, children: React.ReactNode }) {
    return (
        <View style={{
            marginBottom: 24,
            paddingBottom: 16,
            borderBottomWidth: 1,
            borderBottomColor: "rgba(80, 80, 80, 0.5)"
        }}>
            <Label
                text={title}
                style={{
                    fontSize: 18,
                    marginBottom: 12,
                    color: "rgba(180, 180, 180, 1)"
                }}
            />
            {children}
        </View>
    )
}

function Row({ children }: { children: React.ReactNode }) {
    return (
        <View style={{
            flexDirection: "row",
            alignItems: "center",
            flexWrap: "wrap",
            marginBottom: 8
        }}>
            {children}
        </View>
    )
}

function App() {
    const [count, setCount] = useState(0)
    const [textValue, setTextValue] = useState("Edit me!")
    const [toggleA, setToggleA] = useState(true)
    const [toggleB, setToggleB] = useState(false)
    const [sliderValue, setSliderValue] = useState(50)

    return (
        <ScrollView
            mouseWheelScrollSize={6}
            style={{
                flexGrow: 1,
                backgroundColor: "rgba(35, 35, 35, 1)"
            }}
        >
            <View style={{
                padding: 24,
                maxWidth: 800
            }}>
                <Label
                    text="OneJS Control Showcase"
                    style={{
                        fontSize: 28,
                        marginBottom: 8,
                        color: "rgba(255, 255, 255, 1)"
                    }}
                />
                <Text style={{ marginBottom: 24, color: "rgba(160, 160, 160, 1)" }}>
                    Demonstrates all supported UI controls with the runtime theme.
                </Text>

                {/* Text & Label Section */}
                <Section title="Text & Label">
                    <Text>This is a Text component (TextElement)</Text>
                    <Label text="This is a Label component" />
                    <Text style={{ color: "rgba(150, 200, 255, 1)" }}>
                        Styled text with custom color
                    </Text>
                </Section>

                {/* Button Section */}
                <Section title="Button">
                    <Row>
                        <Button text="Default Button" onClick={() => setCount(c => c + 1)} />
                        <Button text="Another Button" onClick={() => {}} />
                    </Row>
                    <Row>
                        <Button
                            text={`Clicked ${count} times`}
                            onClick={() => setCount(c => c + 1)}
                            style={{ minWidth: 150 }}
                        />
                    </Row>
                    <Text style={{ marginTop: 8, fontSize: 12, color: "rgba(140, 140, 140, 1)" }}>
                        Hover over buttons to test hover state styling
                    </Text>
                </Section>

                {/* TextField Section */}
                <Section title="TextField">
                    <TextField
                        value={textValue}
                        onChange={(e) => setTextValue(e.value)}
                        style={{ marginBottom: 8, maxWidth: 300 }}
                    />
                    <TextField
                        value="Read-only field"
                        readOnly={true}
                        style={{ marginBottom: 8, maxWidth: 300 }}
                    />
                    <Text style={{ marginTop: 8, fontSize: 12, color: "rgba(140, 140, 140, 1)" }}>
                        Current value: {textValue}
                    </Text>
                </Section>

                {/* Toggle Section */}
                <Section title="Toggle">
                    <Row>
                        <Toggle
                            label="Toggle A (checked)"
                            value={toggleA}
                            onChange={(e) => setToggleA(e.value)}
                            style={{ marginRight: 24 }}
                        />
                        <Toggle
                            label="Toggle B (unchecked)"
                            value={toggleB}
                            onChange={(e) => setToggleB(e.value)}
                        />
                    </Row>
                    <Text style={{ marginTop: 8, fontSize: 12, color: "rgba(140, 140, 140, 1)" }}>
                        Toggle A: {toggleA ? "ON" : "OFF"} | Toggle B: {toggleB ? "ON" : "OFF"}
                    </Text>
                </Section>

                {/* Slider Section */}
                <Section title="Slider">
                    <View style={{ maxWidth: 400 }}>
                        <Slider
                            lowValue={0}
                            highValue={100}
                            value={sliderValue}
                            onChange={(e) => setSliderValue(e.value)}
                        />
                    </View>
                    <Text style={{ marginTop: 8, fontSize: 12, color: "rgba(140, 140, 140, 1)" }}>
                        Slider value: {sliderValue.toFixed(0)}
                    </Text>
                </Section>

                {/* ScrollView Section */}
                <Section title="ScrollView (nested)">
                    <ScrollView
                        mouseWheelScrollSize={6}
                        style={{
                            height: 120,
                            backgroundColor: "rgba(45, 45, 45, 1)",
                            borderRadius: 4,
                            borderWidth: 1,
                            borderColor: "rgba(60, 60, 60, 1)"
                        }}
                    >
                        <View style={{ padding: 12 }}>
                            {Array.from({ length: 10 }, (_, i) => (
                                <Text key={i} style={{ marginBottom: 8 }}>
                                    Scrollable item {i + 1}
                                </Text>
                            ))}
                        </View>
                    </ScrollView>
                    <Text style={{ marginTop: 8, fontSize: 12, color: "rgba(140, 140, 140, 1)" }}>
                        Scroll to test scrollbar styling
                    </Text>
                </Section>

                {/* View Styling Section */}
                <Section title="View (VisualElement)">
                    <Row>
                        <View style={{
                            width: 80,
                            height: 80,
                            backgroundColor: "rgba(77, 144, 254, 1)",
                            borderRadius: 8,
                            marginRight: 12
                        }} />
                        <View style={{
                            width: 80,
                            height: 80,
                            backgroundColor: "rgba(100, 180, 100, 1)",
                            borderRadius: 40,
                            marginRight: 12
                        }} />
                        <View style={{
                            width: 80,
                            height: 80,
                            borderWidth: 2,
                            borderColor: "rgba(255, 150, 100, 1)",
                            borderRadius: 8
                        }} />
                    </Row>
                    <Text style={{ marginTop: 8, fontSize: 12, color: "rgba(140, 140, 140, 1)" }}>
                        Views with different backgrounds and borders
                    </Text>
                </Section>

                {/* Combined Interactive Demo */}
                <Section title="Interactive Demo">
                    <View style={{
                        padding: 16,
                        backgroundColor: "rgba(50, 50, 50, 1)",
                        borderRadius: 8,
                        borderWidth: 1,
                        borderColor: "rgba(70, 70, 70, 1)"
                    }}>
                        <Row>
                            <Button
                                text="Reset All"
                                onClick={() => {
                                    setCount(0)
                                    setTextValue("Edit me!")
                                    setToggleA(true)
                                    setToggleB(false)
                                    setSliderValue(50)
                                }}
                            />
                        </Row>
                        <View style={{
                            marginTop: 12,
                            padding: 12,
                            backgroundColor: "rgba(40, 40, 40, 1)",
                            borderRadius: 4
                        }}>
                            <Text style={{ fontSize: 12, color: "rgba(180, 180, 180, 1)" }}>
                                State: count={count}, text="{textValue}", toggleA={String(toggleA)}, toggleB={String(toggleB)}, slider={sliderValue.toFixed(0)}
                            </Text>
                        </View>
                    </View>
                </Section>
            </View>
        </ScrollView>
    )
}

render(<App />, __root)

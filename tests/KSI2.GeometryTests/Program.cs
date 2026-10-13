using KSI2;
using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;

PilotLayoutDefinition[] definitions =
[
    .. ThrottleModule.Definitions,
    .. NavballModule.Definitions,
    .. RightIndicatorsModule.Definitions
];
PilotFrameLayout Resolve(float x, float y, float width, float height, PilotUiSettings settings) =>
    PilotFrameLayout.Resolve(x, y, width, height, settings, definitions);

var speedState = new SpeedModeState();
var firstVehicle = new object();
var secondVehicle = new object();
if (!speedState.Observe(firstVehicle, SpeedReadoutMode.Orbit, false) ||
    speedState.Mode != SpeedReadoutMode.Orbit)
    throw new Exception("initial speed mode did not follow the vehicle frame");
if (speedState.SelectNext(false) != SpeedReadoutMode.Surface ||
    speedState.Mode != SpeedReadoutMode.Surface)
    throw new Exception("speed selector did not cycle without a target");
if (speedState.Observe(firstVehicle, SpeedReadoutMode.Surface, false) ||
    speedState.Mode != SpeedReadoutMode.Surface)
    throw new Exception("speed mode changed while observing the same frame");
if (!speedState.Observe(secondVehicle, SpeedReadoutMode.Target, true) ||
    speedState.Mode != SpeedReadoutMode.Target)
    throw new Exception("speed mode did not follow a new vehicle while editing");
speedState.Observe(secondVehicle, SpeedReadoutMode.Target, false);
if (speedState.Mode != SpeedReadoutMode.Orbit)
    throw new Exception("speed mode retained a lost target while editing");

static void Near(float expected, float actual, string name, float epsilon = 0.001f)
{
    if (MathF.Abs(expected - actual) > epsilon) throw new Exception($"{name}: expected {expected}, received {actual}");
}

var cluster = Resolve(0, 0, 1920, 1080, new PilotUiSettings()).Navball;
var speedOrigin = SpeedReadoutGeometry.TopLeft(cluster);
Near(75.85f, speedOrigin.X, "speed card follows current navball x");
Near(864.95f, speedOrigin.Y, "speed card follows current navball y");
var altitudeOrigin = AltitudeReadoutGeometry.TopLeft(cluster);
Near(363.85f, altitudeOrigin.X, "altitude card follows current navball x");
Near(864.95f, altitudeOrigin.Y, "altitude card follows current navball y");
var altitudeFormat = System.Globalization.CultureInfo.InvariantCulture;
if (AltitudeReadoutGeometry.Format(99.999, altitudeFormat) != new AltitudeReadoutValue("100", "M")
    || AltitudeReadoutGeometry.Format(99.99901, altitudeFormat) != new AltitudeReadoutValue("99", "M"))
    throw new Exception("altitude first unit entry uses the serialized float threshold");
if (AltitudeReadoutGeometry.Format(99_998.9, altitudeFormat) != new AltitudeReadoutValue("99998", "M")
    || AltitudeReadoutGeometry.Format(99_999, altitudeFormat) != new AltitudeReadoutValue("99", "KM")
    || AltitudeReadoutGeometry.Format(99_999_000, altitudeFormat) != new AltitudeReadoutValue("99", "MM")
    || AltitudeReadoutGeometry.Format(-99_999, altitudeFormat) != new AltitudeReadoutValue("-99", "KM")
    || AltitudeReadoutGeometry.Format(99_999_000_000, altitudeFormat) is not null)
    throw new Exception("altitude integer units or truncation differs from KSP2 binder");
var speedFormat = System.Globalization.CultureInfo.InvariantCulture;
if (SpeedReadoutGeometry.Format(7_784.4, speedFormat) != new SpeedReadoutValue("7784.4", "m/s"))
    throw new Exception("orbital speed below KSP2 unit switch remains in m/s");
if (SpeedReadoutGeometry.Format(99_998.9, speedFormat) != new SpeedReadoutValue("99998.9", "m/s"))
    throw new Exception("speed below km/s threshold");
if (SpeedReadoutGeometry.Format(99_999, speedFormat) != new SpeedReadoutValue("100.0", "km/s"))
    throw new Exception("speed at km/s threshold");
if (SpeedReadoutGeometry.Format(99_999_000, speedFormat) != new SpeedReadoutValue("100.0", "mm/s"))
    throw new Exception("speed at mm/s threshold");
if (SpeedReadoutGeometry.Format(99_999_000_000, speedFormat) is not null)
    throw new Exception("speed beyond final binder entry should retain prior display");
Near(0f, KsaAtmosphereSource.Gauge(0, 1.225), "vacuum atmosphere gauge");
Near(1f, KsaAtmosphereSource.Gauge(1.225, 1.225), "sea level atmosphere gauge");
Near(0.5f, KsaAtmosphereSource.Gauge(1.225 / 16, 1.225), "quarter-root atmosphere gauge");
Near(0f, KsaAtmosphereSource.Gauge(1, 0), "airless body atmosphere gauge");
Near(0f, KsaAtmosphereSource.Gauge(double.NaN, 1), "invalid atmosphere density");
var needleStart = AtmosphericIndicatorGeometry.Needle(0);
Near(53.5f, needleStart.Position.X, "needle minimum x");
Near(11.9f, needleStart.Position.Y, "needle minimum screen y");
Near(0f, needleStart.AngleDegrees, "needle minimum rotation");
var needleMiddle = AtmosphericIndicatorGeometry.Needle(0.5f);
Near(-6.025f, needleMiddle.Position.X, "needle Bezier midpoint x");
Near(-2.325f, needleMiddle.Position.Y, "needle Bezier midpoint screen y");
var rocketEnd = AtmosphericIndicatorGeometry.Rocket(1);
Near(-65.7f, rocketEnd.Position.X, "rocket maximum x");
Near(-25.5f, rocketEnd.Position.Y, "rocket maximum screen y");
Near(-41.395893f, rocketEnd.AngleDegrees, "rocket maximum quaternion rotation");
Near(rocketEnd.Position.X, AtmosphericIndicatorGeometry.Rocket(1.2f).Position.X,
    "rocket path clamps beyond maximum");
var widgetOrigin = AtmosphericIndicatorGeometry.WidgetOrigin(cluster);
Near(391.85f, widgetOrigin.X, "atmosphere widget follows navball cluster x");
Near(901.95f, widgetOrigin.Y, "atmosphere widget follows navball cluster y");
var clusterOrigin = Resolve(0, 0, 1920, 1080, new PilotUiSettings()).Navball;
Near(279f, widgetOrigin.X - clusterOrigin.Min.X, "authored atmosphere offset from navball x");
Near(5f, clusterOrigin.Min.Y + clusterOrigin.PanelSize - widgetOrigin.Y - AtmosphericIndicatorGeometry.WidgetHeight,
    "authored atmosphere offset from navball bottom");
var verticalSpeedOrigin = VerticalSpeedIndicatorGeometry.WidgetOrigin(cluster);
Near(widgetOrigin.X, verticalSpeedOrigin.X, "vertical speed aligns with atmosphere");
Near(8f, widgetOrigin.Y - verticalSpeedOrigin.Y - VerticalSpeedIndicatorGeometry.WidgetHeight,
    "vertical speed gap above atmosphere");
var descendingMarker = VerticalSpeedIndicatorGeometry.Marker(-1200);
Near(59f, descendingMarker.Position.X, "vertical speed minimum locator x");
Near(131f, descendingMarker.Position.Y, "vertical speed minimum locator y");
Near(5f, descendingMarker.AngleDegrees, "vertical speed minimum locator angle");
var ascendingMarker = VerticalSpeedIndicatorGeometry.Marker(1200);
Near(16f, ascendingMarker.Position.X, "vertical speed maximum locator x");
Near(24f, ascendingMarker.Position.Y, "vertical speed maximum locator y");
Near(45f, ascendingMarker.AngleDegrees, "vertical speed maximum locator angle");
var zeroSpeedMarker = VerticalSpeedIndicatorGeometry.Marker(0);
  var zeroOffsetMarker = VerticalSpeedIndicatorGeometry.OffsetMarker(zeroSpeedMarker, 0f);
  Near(zeroSpeedMarker.Position.X, zeroOffsetMarker.Position.X, "zero needle offset keeps x");
  Near(zeroSpeedMarker.Position.Y, zeroOffsetMarker.Position.Y, "zero needle offset keeps y");
  var shiftedMarker = VerticalSpeedIndicatorGeometry.OffsetMarker(ascendingMarker, 2f);
  Near(ascendingMarker.Position.X + 2f * MathF.Sin(ascendingMarker.AngleDegrees * MathF.PI / 180f),
      shiftedMarker.Position.X, "rotated needle offset x");
  Near(ascendingMarker.Position.Y + 2f * MathF.Cos(ascendingMarker.AngleDegrees * MathF.PI / 180f),
      shiftedMarker.Position.Y, "rotated needle offset y");
var positiveTenMarker = VerticalSpeedIndicatorGeometry.Marker(10);
var positiveHalfMarker = VerticalSpeedIndicatorGeometry.Marker(5);
Near((VerticalSpeedIndicatorGeometry.TipY(zeroSpeedMarker)
    + VerticalSpeedIndicatorGeometry.TipY(positiveTenMarker)) / 2f,
    VerticalSpeedIndicatorGeometry.TipY(positiveHalfMarker), "positive inner interval uses pixel distance");
var negativeTenMarker = VerticalSpeedIndicatorGeometry.Marker(-10);
var negativeHalfMarker = VerticalSpeedIndicatorGeometry.Marker(-5);
Near((VerticalSpeedIndicatorGeometry.TipY(zeroSpeedMarker)
    + VerticalSpeedIndicatorGeometry.TipY(negativeTenMarker)) / 2f,
    VerticalSpeedIndicatorGeometry.TipY(negativeHalfMarker), "negative inner interval uses pixel distance");
var hundredSpeedMarker = VerticalSpeedIndicatorGeometry.Marker(100);
Near(VerticalSpeedIndicatorGeometry.GuideMarker(5,
        VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds).Position.X,
    hundredSpeedMarker.Position.X, "calibrated positive hundred mark");
for (var i = 0; i < VerticalSpeedIndicatorGeometry.MarkPaths.Length; i++)
    Near(VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.Marker(
            VerticalSpeedIndicatorGeometry.PrintedSpeeds[i])),
        VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(
            i, VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds)), $"vertical speed mark {i} reaches its guide");
var changedMarkSpeeds = (float[])VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds.Clone();
changedMarkSpeeds[5] = 350f;
Near(VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(5, changedMarkSpeeds)),
    VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.Marker(100, changedMarkSpeeds)),
    "adjusted positive hundred speed moves the needle to its guide");
if (VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(5, changedMarkSpeeds))
    >= VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(5,
        VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds)))
    throw new Exception("changing a mark speed did not move its red guide");
var movedEndpoints = (float[])VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds.Clone();
movedEndpoints[0] = -15000f;
movedEndpoints[6] = 15000f;
if (VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(0, movedEndpoints))
    <= VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(0,
        VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds)))
    throw new Exception("negative endpoint calibration did not move its guide");
if (VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(6, movedEndpoints))
    >= VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(6,
        VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds)))
    throw new Exception("positive endpoint calibration did not move its guide");
foreach (var calibration in new[] { VerticalSpeedIndicatorGeometry.DefaultMarkSpeeds, changedMarkSpeeds })
{
    var actualSpeeds = new[] { -1000f, -100f, -10f, 0f, 10f, 100f, 1000f };
    for (var i = 0; i < actualSpeeds.Length; i++)
    {
        var needle = VerticalSpeedIndicatorGeometry.OffsetMarker(
            VerticalSpeedIndicatorGeometry.Marker(actualSpeeds[i], calibration), 2f);
        var guide = VerticalSpeedIndicatorGeometry.OffsetMarker(
            VerticalSpeedIndicatorGeometry.GuideMarker(i, calibration), 2f);
        Near(guide.Position.X, needle.Position.X, $"printed speed {actualSpeeds[i]} meets guide x");
        Near(guide.Position.Y, needle.Position.Y, $"printed speed {actualSpeeds[i]} meets guide y");
        Near(guide.AngleDegrees, needle.AngleDegrees, $"printed speed {actualSpeeds[i]} meets guide angle");
    }
    var middle = VerticalSpeedIndicatorGeometry.Marker(55, calibration);
    Near((VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(4, calibration))
        + VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(5, calibration))) / 2f,
        VerticalSpeedIndicatorGeometry.TipY(middle), "55 m/s is halfway in pixels between 10 and 100");
}
for (var i = 0; i < 7; i += 6)
    Near(VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.GuideMarker(i, movedEndpoints)),
        VerticalSpeedIndicatorGeometry.TipY(VerticalSpeedIndicatorGeometry.Marker(
            VerticalSpeedIndicatorGeometry.PrintedSpeeds[i], movedEndpoints)),
        $"moving endpoint {i} keeps the needle on its guide");
var restoredCalibration = PilotUiSettings.Parse(JsonSerializer.Serialize(new PilotUiSettings
{
    VerticalSpeedMarkSpeeds = changedMarkSpeeds,
    VerticalSpeedNeedleOffset = 2.5f
}));
Near(350f, restoredCalibration.VerticalSpeedMarkSpeeds[5], "positive hundred calibration survives settings parse");
Near(2.5f, restoredCalibration.VerticalSpeedNeedleOffset, "rotated needle offset survives settings parse");
var rocketAtTop = AtmosphericIndicatorGeometry.RocketOnWidget(0);
Near(75.5f, rocketAtTop.Position.X, "vacuum rocket widget x");
Near(19.5f, rocketAtTop.Position.Y, "vacuum rocket widget y");
var rocketAtBottom = AtmosphericIndicatorGeometry.RocketOnWidget(1);
Near(25.6f, rocketAtBottom.Position.X, "sea level rocket widget x");
Near(129.4f, rocketAtBottom.Position.Y, "sea level rocket widget y");
var previousRocket = rocketAtTop;
for (var i = 1; i <= 100; i++)
{
    var rocket = AtmosphericIndicatorGeometry.RocketOnWidget(i / 100f);
    if (rocket.Position.X > previousRocket.Position.X || rocket.Position.Y < previousRocket.Position.Y
        || rocket.AngleDegrees > previousRocket.AngleDegrees)
        throw new Exception($"atmosphere rocket reversed direction at {i}%");
    var dx = rocket.Position.X - previousRocket.Position.X;
    var dy = rocket.Position.Y - previousRocket.Position.Y;
    if (MathF.Sqrt(dx * dx + dy * dy) > 2f)
        throw new Exception($"atmosphere rocket jumped at {i}%");
    previousRocket = rocket;
}
Near(112.85f, cluster.Min.X, "cluster left");
Near(746.95f, cluster.Min.Y, "cluster top");
Near(262.85f, cluster.Center.X, "navball center x");
Near(896.95f, cluster.Center.Y, "navball center y");
Near(88f, cluster.BallRadius, "navball radius");
if (NavballClusterLayout.BackgroundColor != 0xFF190B03u)
    throw new Exception("cluster background color mismatch");
var scaledCluster = Resolve(10, 20, 1280, 720, new PilotUiSettings()).Navball;
Near(10f + cluster.Min.X * 2f / 3f, scaledCluster.Min.X, "scaled cluster left");
Near(20f + cluster.Min.Y * 2f / 3f, scaledCluster.Min.Y, "scaled cluster top");
Near(88f * 2f / 3f, scaledCluster.BallRadius, "scaled navball radius");

// Saved offsets move every module by the same screen-space delta, including
// when the host viewport begins away from (0, 0).
foreach (var (vx, vy, width, height) in new[]
    { (0f, 0f, 1920f, 1080f), (120f, 45f, 1280f, 720f), (75f, 90f, 900f, 1200f) })
{
    var baseline = Resolve(vx, vy, width, height, new PilotUiSettings());
    var saved = new PilotUiSettings { PositionSet = true, X = 0.35f, Y = 0.62f };
    var moved = Resolve(vx, vy, width, height, saved);
    var dx = moved.Throttle.Position.X - baseline.Throttle.Position.X;
    var dy = moved.Throttle.Position.Y - baseline.Throttle.Position.Y;
    Near(vx + 0.35f * moved.Throttle.AvailableX, moved.Throttle.Position.X, "saved throttle x");
    Near(vy + 0.62f * moved.Throttle.AvailableY, moved.Throttle.Position.Y, "saved throttle y");
    Near(baseline.Navball.Min.X + dx, moved.Navball.Min.X, "saved navball x");
    Near(baseline.Navball.Min.Y + dy, moved.Navball.Min.Y, "saved navball y");
    Near(baseline.Component(RightIndicatorsModule.AtmosphereId).Position.X + dx,
        moved.Component(RightIndicatorsModule.AtmosphereId).Position.X, "saved atmosphere x");
    Near(baseline.Component(RightIndicatorsModule.VerticalSpeedId).Position.Y + dy,
        moved.Component(RightIndicatorsModule.VerticalSpeedId).Position.Y, "saved vertical speed y");
    Near(MathF.Min(moved.Throttle.Position.X, moved.Navball.Min.X - 37f * moved.Navball.Scale),
        moved.EditorMin.X, "editor includes module extents");
    var duringDrag = baseline.Translate(new Point(dx, dy));
    Near(moved.Throttle.Position.X, duringDrag.Throttle.Position.X, "drag moves throttle before drawing");
    Near(moved.Component(NavballModule.SpeedCardId).Position.Y,
        duringDrag.Component(NavballModule.SpeedCardId).Position.Y, "drag moves speed card before drawing");
    Near(moved.EditorMax.X, duringDrag.EditorMax.X, "drag moves editor extent before drawing");
}
foreach (var scale in new[] { 0.5f, 1f, 2f })
{
    var layout = Resolve(20, 30, 1920, 1080, new PilotUiSettings { Scale = scale });
    Near(scale, layout.Throttle.Scale, "shared throttle scale");
    Near(scale, layout.Navball.Scale, "shared navball scale");
}
var constrained = Resolve(10, 20, 160, 240, new PilotUiSettings { Scale = 2 });
if (constrained.Throttle.Width > 160 || constrained.Throttle.Height > 240 ||
    constrained.Navball.PanelSize > 160 || constrained.Navball.PanelSize > 240)
    throw new Exception("scale did not fit a small viewport");
var extendedDefinitions = definitions.Append(new PilotLayoutDefinition("new-panel",
    (layout, _) => new(new(layout.ViewportOrigin.X + 600f, layout.ViewportOrigin.Y + 100f),
        layout.Navball.Scale, 120f, 80f), true)).ToArray();
var extended = PilotFrameLayout.Resolve(50, 20, 1920, 1080, new PilotUiSettings(), extendedDefinitions);
Near(650f, extended.Component("new-panel").Position.X, "registered panel uses viewport origin");
Near(770f, extended.EditorMax.X, "registered panel extends combined editor bounds");
Near(cluster.Min.X + 50f, extended.Navball.Min.X, "new panel leaves existing roots unchanged");
var dragLayout = Resolve(120, 45, 1280, 720, new PilotUiSettings());
var drag = new CombinedLayoutDrag();
drag.Begin(new(300, 400), dragLayout.Throttle);
var dragged = drag.Move(new(400, 450), dragLayout.ViewportOrigin, dragLayout.Throttle);
Near((dragLayout.Throttle.Position.X + 100f - 120f) / dragLayout.Throttle.AvailableX,
    dragged.X, "combined drag saves normalized x");
Near(Math.Clamp((dragLayout.Throttle.Position.Y + 50f - 45f) / dragLayout.Throttle.AvailableY, 0f, 1f),
    dragged.Y, "combined drag saves normalized y");
Near(1f, drag.Move(new(10000, 10000), dragLayout.ViewportOrigin, dragLayout.Throttle).X,
    "combined drag clamps at viewport edge");
if (!drag.End() || drag.End()) throw new Exception("completed drag persisted more than once");
drag.Begin(new(300, 400), dragLayout.Throttle);
drag.Cancel();
if (drag.End()) throw new Exception("cancelled drag was treated as complete");

// Verify that equator clipping normalization produces an exact unit circle silhouette.
for (var angleDeg = 0; angleDeg < 360; angleDeg += 15)
{
    var rad = angleDeg * MathF.PI / 180f;
    var p1 = new System.Numerics.Vector3(MathF.Cos(rad) * 0.9f, MathF.Sin(rad) * 0.9f, 0.43588989f);
    var p2 = new System.Numerics.Vector3(MathF.Cos(rad) * 0.9f, MathF.Sin(rad) * 0.9f, -0.43588989f);
    var t = p1.Z / (p1.Z - p2.Z);
    var pos = System.Numerics.Vector3.Lerp(p1, p2, t);
    Near(0f, pos.Z, "interpolated z is on equator plane");
    var len = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
    if (len > 1e-4f) pos = new System.Numerics.Vector3(pos.X / len, pos.Y / len, 0f);
    Near(1f, MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y), "normalized equator radius");
}


for (var i = 0; i < ThrottleGeometry.Nodes.Length; i++)
{
    var throttle = i / 10f;
    var actual = ThrottleGeometry.Handle(throttle);
    var expected = ThrottleGeometry.Nodes[i];
    Near(expected.Position.X, actual.Position.X, $"handle node {throttle} x");
    Near(expected.Position.Y, actual.Position.Y, $"handle node {throttle} y");
    Near(expected.AngleDegrees, actual.AngleDegrees, $"handle node {throttle} angle");
}

foreach (var throttle in new[] { 0.05f, 0.25f, 0.45f, 0.49f, 0.5f, 0.51f, 0.75f, 0.95f })
{
    var pose = ThrottleGeometry.Handle(throttle);
    if (!float.IsFinite(pose.Position.X) || !float.IsFinite(pose.Position.Y) || !float.IsFinite(pose.AngleDegrees))
        throw new Exception($"non-finite interpolated pose at {throttle}");
    if (MathF.Abs(pose.Position.Y) > 22) throw new Exception($"vertical travel exceeded at {throttle}");
}
Near(21, ThrottleGeometry.Handle(0).Position.Y, "zero handle vertical node");
Near(-21, ThrottleGeometry.Handle(1).Position.Y, "full handle vertical node");
var beforeHalf = ThrottleGeometry.Handle(0.499f);
var afterHalf = ThrottleGeometry.Handle(0.501f);
if (MathF.Abs(afterHalf.AngleDegrees - beforeHalf.AngleDegrees) > 2) throw new Exception("handle angle is discontinuous around 50%");

Near(0, ThrottleGeometry.RadialFill(0), "empty radial fill");
Near(0.0625f, ThrottleGeometry.RadialFill(0.25f), "quarter throttle fill");
Near(0.125f, ThrottleGeometry.RadialFill(0.5f), "half throttle fill");
Near(0.1875f, ThrottleGeometry.RadialFill(0.75f), "three-quarter throttle fill");
Near(0.25f, ThrottleGeometry.RadialFill(1), "full radial fill");
if (ThrottleGeometry.Percent(0) != "0" || ThrottleGeometry.Percent(1) != "100") throw new Exception("percentage endpoints are incorrect");
if (!KsaThrottleSource.TryNormalize(0, out var zero) || zero.Value != 0) throw new Exception("zero throttle was rejected");
if (!KsaThrottleSource.TryNormalize(1, out var full) || full.Value != 1) throw new Exception("full throttle was rejected");
if (KsaThrottleSource.TryNormalize(float.NaN, out _)) throw new Exception("NaN telemetry was accepted");
if (!KsaThrottleSource.TryNormalize(-3, out var low) || low.Value != 0) throw new Exception("low throttle was not clamped");
if (!KsaThrottleSource.TryNormalize(4, out var high) || high.Value != 1) throw new Exception("high throttle was not clamped");
var malformedSettings = PilotUiSettings.Parse("{ not valid json");
if (!malformedSettings.Show || malformedSettings.Scale != 1 || malformedSettings.Editing) throw new Exception("malformed settings did not use defaults");
var clampedSettings = PilotUiSettings.Parse("{\"show\":false,\"scale\":5,\"x\":-2,\"y\":3,\"editing\":true}");
if (clampedSettings.Show || clampedSettings.Scale != 2 || clampedSettings.X != 0 || clampedSettings.Y != 1 || clampedSettings.Editing)
    throw new Exception("settings were not clamped or edit mode did not start locked");
var serialized = JsonSerializer.Serialize(new PilotUiSettings
{
    Show = false, Scale = 1.75f, X = 0.35f, Y = 0.62f, PositionSet = true,
    MouseThrottleControl = true, BlurOpacityTicks = 240, Editing = true
});
foreach (var field in new[] { "Show", "Scale", "X", "Y", "PositionSet", "MouseThrottleControl", "BlurOpacityTicks" })
    if (!serialized.Contains($"\"{field}\"", StringComparison.Ordinal))
        throw new Exception($"settings field {field} changed name");
var reloaded = PilotUiSettings.Parse(serialized);
if (reloaded.Show || reloaded.Scale != 1.75f || reloaded.X != 0.35f || reloaded.Y != 0.62f ||
    !reloaded.PositionSet || !reloaded.MouseThrottleControl || reloaded.BlurOpacityTicks != 240 || reloaded.Editing)
    throw new Exception("saved settings did not survive reload");
reloaded.Reset();
if (!reloaded.Show || reloaded.Scale != 1f || reloaded.PositionSet ||
    reloaded.X != 0.01355f || reloaded.Y != 0.97f || !reloaded.MouseThrottleControl)
    throw new Exception("reset changed settings outside the existing layout fields");
var marker = Path.Combine(Path.GetTempPath(), $"ksi2-blur-test-{Guid.NewGuid():N}");
try
{
    var blurSettings = new PilotUiSettings { BlurPanelBackgrounds = true };
    var blurGuard = new BlurCrashGuard(blurSettings, () => true, marker);
    if (!File.Exists(marker) || !blurGuard.Enabled) throw new Exception("blur marker was not armed");
    blurGuard.OnCleanUnload();
    if (File.Exists(marker)) throw new Exception("clean unload retained the blur marker");
    File.WriteAllText(marker, "unclean exit");
    var recovered = new BlurCrashGuard(blurSettings, () => true, marker);
    if (recovered.Enabled || !recovered.RecoveredAfterCrash || File.Exists(marker))
        throw new Exception("unclean blur session was not recovered");
}
finally
{
    if (File.Exists(marker)) File.Delete(marker);
}

var hudMarker = Path.Combine(Path.GetTempPath(), $"ksi2-hud-menu-test-{Guid.NewGuid():N}");
try
{
    var hudSettings = new PilotUiSettings();
    using (var disabled = new HudMenuIntegration(hudSettings, () => true, hudMarker))
    {
        disabled.Initialize();
        if (typeof(KSI2Mod).Assembly.GetReferencedAssemblies()
            .Any(assembly => assembly.Name is "0Harmony" or "KSI2.HudMenuPatch"))
            throw new Exception("main KSI2 assembly references optional patch code while disabled");
        if (disabled.Enabled || File.Exists(hudMarker) ||
            AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                assembly.GetName().Name is "0Harmony" or "KSI2.HudMenuPatch"))
            throw new Exception("disabled HUD menu integration loaded patch code or armed crash recovery");
    }
    File.WriteAllText(hudMarker, "unclean exit");
    hudSettings.ExperimentalHudMenu = true;
    hudSettings.ConfigurationVisible = false;
    using var recoveredHudMenu = new HudMenuIntegration(hudSettings, () => true, hudMarker);
    if (recoveredHudMenu.Enabled || !recoveredHudMenu.RecoveredAfterCrash ||
        !hudSettings.ConfigurationVisible || File.Exists(hudMarker))
        throw new Exception("unclean HUD menu session was not recovered");
}
finally
{
    if (File.Exists(hudMarker)) File.Delete(hudMarker);
}

AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory,
    "assets", "optional", "0Harmony.dll"));
var patchAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory,
    "assets", "optional", "KSI2.HudMenuPatch.dll"));
var patchType = patchAssembly.GetType("KSI2.Optional.HarmonyHudMenuPatch", true)!;
var patchFlags = BindingFlags.Static | BindingFlags.Public;
var probeMethod = typeof(HudMenuPatchProbe).GetMethod(nameof(HudMenuPatchProbe.Run))!;
var observedPatch = false;
patchType.GetMethod("Install", patchFlags)!.Invoke(null, [probeMethod, (Action)(() => observedPatch = true)]);
HudMenuPatchProbe.Run();
if (!observedPatch) throw new Exception("Harmony did not invoke the HUD menu postfix on .NET 10");
patchType.GetMethod("Uninstall", patchFlags)!.Invoke(null, null);
observedPatch = false;
HudMenuPatchProbe.Run();
if (observedPatch) throw new Exception("Harmony retained the HUD menu postfix after disabling it");

Console.WriteLine("Throttle geometry and telemetry checks passed.");


Near(269, ThrottleGeometry.HandleCenter(0).Y, "complete slider travel at zero");
Near(145, ThrottleGeometry.HandleCenter(.5f).Y, "complete slider travel at half");
Near(21, ThrottleGeometry.HandleCenter(1).Y, "complete slider travel at full");
Span<Point> polygon = stackalloc Point[8];
Near(0, ThrottleGeometry.ClipFill(0, 0, 85, 290, 0, polygon), "empty sector emits no geometry");
float Area(ReadOnlySpan<Point> points)
{
    float area = 0;
    for (var i = 0; i < points.Length; i++)
    {
        var a = points[i]; var b = points[(i + 1) % points.Length];
        area += a.X * b.Y - a.Y * b.X;
    }
    return MathF.Abs(area) / 2;
}
float previousArea = 0;
for (var i = 0; i <= 1000; i++)
{
    var t = i / 1000f;
    var count = ThrottleGeometry.ClipFill(0, 0, 85, 290, t, polygon);
    foreach (var point in polygon[..count])
        if (point.X < -.001f || point.X > 85.001f || point.Y < -.001f || point.Y > 290.001f)
            throw new Exception("Sector clipping escaped the source rectangle");
    var area = Area(polygon[..count]);
    if (area + .01 < previousArea) throw new Exception("Fill area decreases during increasing throttle");
    previousArea = area;
    var center = ThrottleGeometry.HandleCenter(t);
    var rotation = -(ThrottleGeometry.Handle(t).AngleDegrees + 180) * MathF.PI / 180;
    foreach (var corner in new Point[] { new(-32.5f, -17), new(32.5f, -17), new(32.5f, 17), new(-32.5f, 17) })
    {
        var px = 25 + center.X + corner.X * MathF.Cos(rotation) - corner.Y * MathF.Sin(rotation);
        var py = 14 + center.Y + corner.X * MathF.Sin(rotation) + corner.Y * MathF.Cos(rotation);
        if (px < 0 || px > 175 || py < 0 || py > 318) throw new Exception($"Handle outside overlay bounds at {t}");
    }
}
// Independent expected direction: bottom fills before the top.
if (ThrottleGeometry.ClipFill(55, 250, 1, 1, .5f, polygon) == 0 ||
    ThrottleGeometry.ClipFill(55, 35, 1, 1, .5f, polygon) != 0)
    throw new Exception("Radial fill direction is reversed");
Console.WriteLine("Continuous clipping, fill direction and complete overlay bounds passed.");

// Optional real-asset checks/preview, fed by tools/Preview-Throttle.ps1 so these
// tests never need a Vulkan device, game process, or native texture decoder.
if (args.Length > 0)
{
    var directory = args[0];
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "throttle-assets.json")));
    var bitmaps = new Dictionary<string, RasterArtwork.Bitmap>();
    foreach (var texture in json.RootElement.GetProperty("textures").EnumerateArray())
    {
        var name = texture.GetProperty("name").GetString()!;
        bitmaps[name] = new(texture.GetProperty("width").GetInt32(), texture.GetProperty("height").GetInt32(),
            File.ReadAllBytes(Path.Combine(directory, name + ".rgba")));
    }
    var art = RasterArtwork.Prepare(bitmaps);
    if (art.FillRuns.Count == 0) throw new Exception("No masked fill artwork");
    if (!art.Layer("[asset]").Any(r => (r.Color & 0xFFFFFF) < 0x202020))
        throw new Exception("Handle lost its dark original RGB pixels");
    if (art.Layer("[asset]").Count == 0)
        throw new Exception("Original bottom percentage glyph was not imported");
    foreach (var run in art.FillRuns)
        if ((run.Color & 255) != 0 || ((run.Color >> 8) & 255) is < 80 or > 255 || ((run.Color >> 16) & 255) is < 40 or > 102 ||
            run.X < 0 || run.Y < 0 || run.X + run.Width > 85.001 || run.Y + run.Height > 290.001)
            throw new Exception("Fill mask bounds or color mismatch");
    var frames = new List<object>();
    foreach (var t in new[] { 0f, .25f, .5f, .75f, 1f })
    {
        var primitives = new List<object>();
        void Add(RasterArtwork.Run run, bool handle = false)
        {
            var corners = new Point[] { new(run.X, run.Y), new(run.X + run.Width, run.Y),
                new(run.X + run.Width, run.Y + run.Height), new(run.X, run.Y + run.Height) };
            if (handle)
            {
                var center = ThrottleGeometry.HandleCenter(t);
                var angle = -(ThrottleGeometry.Handle(t).AngleDegrees + 180) * MathF.PI / 180;
                corners = corners.Select(p => new Point(center.X + (p.X - 32.5f) * MathF.Cos(angle) - (p.Y - 17) * MathF.Sin(angle),
                    center.Y + (p.X - 32.5f) * MathF.Sin(angle) + (p.Y - 17) * MathF.Cos(angle))).ToArray();
            }
            primitives.Add(new { points = corners, color = run.Color });
        }
        foreach (var name in new[] { "[asset]", "[asset]", "[asset]", "[asset]" })
            foreach (var run in art.Layer(name)) Add(run);
        foreach (var run in art.FillRuns)
        {
            var count = ThrottleGeometry.ClipFill(run.X, run.Y, run.Width, run.Height, t, polygon);
            if (count >= 3) primitives.Add(new { points = polygon[..count].ToArray(), color = run.Color });
        }
        foreach (var run in art.Layer("[asset]"))
        {
            var lx = run.X - ThrottleGeometry.PercentGlyphWidth / 2;
            var ly = run.Y - ThrottleGeometry.PercentGlyphHeight / 2;
            const float glyphAngle = ThrottleGeometry.PercentGlyphAngleDegrees * MathF.PI / 180f;
            var cos = MathF.Cos(glyphAngle);
            var sin = MathF.Sin(glyphAngle);
            var rx = ThrottleGeometry.PercentGlyphCenterX + (lx * cos - ly * sin);
            var ry = ThrottleGeometry.PercentGlyphCenterY + (lx * sin + ly * cos);
            var count = ThrottleGeometry.ClipFill(rx, ry, run.Width, run.Height, t, polygon);
            if (count >= 3) primitives.Add(new { points = polygon[..count].ToArray(), color = run.Color });
        }
        var handleCenter = ThrottleGeometry.HandleCenter(t);
        var handleAngle = -(ThrottleGeometry.Handle(t).AngleDegrees + 180) * MathF.PI / 180;
        foreach (var run in art.SampleHandle(1, handleAngle)) Add(run with { X = run.X + handleCenter.X, Y = run.Y + handleCenter.Y });
        frames.Add(new { throttle = t, handle = handleCenter, angle = handleAngle, primitives });
    }
    File.WriteAllText(Path.Combine(directory, "preview.json"), JsonSerializer.Serialize(frames));
    Console.WriteLine($"Real artwork checks passed; {art.RunCount} cached runs, preview geometry exported.");
}

internal static class HudMenuPatchProbe
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Run() { }
}

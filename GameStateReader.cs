using System.Globalization;
using System.Reflection;
using System.Text;
using KSA;

namespace KSAAdvisor;

public class GameStateReader
{
    private static readonly HashSet<string> ImportantBodies = new()
    {
        "Earth", "Luna", "Mars", "Venus", "Jupiter",
        "Saturn", "Phobos", "Deimos", "Titan", "Europa"
    };

    private string? _cachedSystemPrompt;
    private int     _cachedBodyCount = -1;

    private static Vehicle? GetVehicle()
    {
        var field = typeof(Program).GetField(
            "ControlledVehicle",
            BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null) as Vehicle;
    }

    // ── Статический промт (кешируется) ────────────────────────────────────
    // Содержит: роль + правила + орбитальная структура планет + SOI
    // Пересчитывается только при изменении состава тел в системе

    public string BuildStaticSystemPrompt()
    {
        var promptPath = Path.Combine(Config.ModDir, "prompt.txt");
        var basePrompt = File.Exists(promptPath)
            ? File.ReadAllText(promptPath, Encoding.UTF8)
            : DefaultPrompt;

        int bodyCount = GetBodyCount();
        if (_cachedSystemPrompt != null && bodyCount == _cachedBodyCount)
            return _cachedSystemPrompt;

        _cachedBodyCount = bodyCount;

        var sb = new StringBuilder(basePrompt);
        sb.AppendLine();
        sb.AppendLine("CELESTIAL BODIES IN SYSTEM (orbital structure, not current positions):");

        try
        {
            var system = Universe.CurrentSystem;
            if (system != null)
            {
                foreach (var astro in system.All.AsSpan().ToArray())
                {
                    if (astro is Vehicle || astro is KittenEva)         continue;
                    if (!ImportantBodies.Contains(astro.Id))            continue;
                    if (astro is not IOrbiter orb || orb.Orbit == null) continue;

                    var apo  = ToDouble(orb.Orbit.Apoapsis);
                    var peri = ToDouble(orb.Orbit.Periapsis);
                    var per  = ToDouble(orb.Orbit.Period);

                    // Полные орбитальные элементы + физические параметры тела
                    var extras = new System.Text.StringBuilder();

                    if (astro is Celestial cel)
                    {
                        // Форма орбиты
                        extras.Append($", ecc {cel.Eccentricity:F4}");

                        // Ориентация орбиты (в градусах)
                        if (double.IsFinite(cel.Inclination))
                            extras.Append($", inc {cel.Inclination * 180.0 / Math.PI:F2}°");
                        if (double.IsFinite(cel.LongitudeOfAscendingNode))
                            extras.Append($", LAN {cel.LongitudeOfAscendingNode * 180.0 / Math.PI:F2}°");
                        if (double.IsFinite(cel.ArgumentOfPeriapsis))
                            extras.Append($", AoP {cel.ArgumentOfPeriapsis * 180.0 / Math.PI:F2}°");

                        // Физические параметры
                        extras.Append($", radius {cel.MeanRadius / 1000.0:F0} km");

                        if (cel.Mass > 0)
                        {
                            const double G = 6.6743e-11;
                            var mu = G * cel.Mass;
                            extras.Append($", μ {mu:E3} m³/s²");
                        }

                        // Сфера тяготения
                        if (double.IsFinite(cel.SphereOfInfluence) && cel.SphereOfInfluence > 0)
                            extras.Append($", SOI {FmtDist(cel.SphereOfInfluence)}");
                    }

                    sb.AppendLine(
                        $"  {astro.Id}: orbit {FmtDist(peri)}–{FmtDist(apo)}, " +
                        $"period {FmtPeriod(per)}, around {orb.Parent?.Id ?? "?"}{extras}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Error reading system: {ex.Message}]");
            AdvisorMod.Log($"System prompt error: {ex.Message}");
        }

        _cachedSystemPrompt = sb.ToString();
        return _cachedSystemPrompt;
    }

    // ── Динамическое сообщение (каждый запрос) ────────────────────────────
    // Содержит: время + аномалии планет + другие аппараты + полная телеметрия

    public string BuildDynamicUserMessage(string question)
    {
        var sb = new StringBuilder();

        // Время
        try
        {
            var elapsed = Universe.GetElapsedSimTime();
            sb.AppendLine($"CURRENT GAME TIME: {elapsed.Seconds() / 86400.0:F1} days since simulation start");
        }
        catch { }

        // Текущие аномалии планет
        try
        {
            var system = Universe.CurrentSystem;
            if (system != null)
            {
                sb.AppendLine("CURRENT PLANET POSITIONS (true anomaly from perihelion):");
                foreach (var astro in system.All.AsSpan().ToArray())
                {
                    if (astro is Vehicle || astro is KittenEva)         continue;
                    if (!ImportantBodies.Contains(astro.Id))            continue;
                    if (astro is not IOrbiter orb || orb.Orbit == null) continue;

                    var ta    = orb.Orbit.GetTrueAnomaly();
                    var taVal = ta.Value();
                    var deg   = double.IsFinite(taVal)
                        ? $"{taVal * 180.0 / Math.PI:F1}°" : "?";
                    sb.AppendLine($"  {astro.Id}: anomaly {deg}");
                }
            }
        }
        catch { }

        // Другие аппараты (станции, зонды)
        try
        {
            var controlled = GetVehicle();
            var system     = Universe.CurrentSystem;
            if (system != null)
            {
                var others = system.All.AsSpan().ToArray()
                    .OfType<Vehicle>()
                    .Where(v => v != controlled && v.Orbit != null)
                    .Take(10)
                    .ToList();

                if (others.Count > 0)
                {
                    sb.AppendLine("OTHER VESSELS IN SYSTEM:");
                    foreach (var v in others)
                    {
                        var r    = ToDouble(v.Orbit!.Parent.MeanRadius);
                        var apo  = (ToDouble(v.Orbit.Apoapsis)  - r) / 1000.0;
                        var peri = (ToDouble(v.Orbit.Periapsis) - r) / 1000.0;
                        sb.AppendLine($"  {v.Id}: {apo:F0}×{peri:F0} km around {v.Orbit.Parent?.Id ?? "?"}");
                    }
                }
            }
        }
        catch { }

        // Полная телеметрия текущего корабля
        sb.AppendLine("CURRENT VESSEL TELEMETRY:");
        try
        {
            var v = GetVehicle();
            if (v?.Orbit != null)
            {
                var r    = ToDouble(v.Orbit.Parent.MeanRadius);
                var apo  = (ToDouble(v.Orbit.Apoapsis)  - r) / 1000.0;
                var peri = (ToDouble(v.Orbit.Periapsis) - r) / 1000.0;

                // Орбитальные параметры
                sb.AppendLine($"  Apoapsis:       {apo:F0} km");
                sb.AppendLine($"  Periapsis:      {peri:F0} km");
                sb.AppendLine($"  Orbital speed:  {v.OrbitalSpeed:F0} m/s");
                sb.AppendLine($"  Surface speed:  {v.GetSurfaceSpeed():F0} m/s");
                sb.AppendLine($"  Orbiting:       {v.Orbit.Parent?.Id ?? "?"}");

                // Высота — два варианта
                // GetBarometricAltitude: высота над средним радиусом (сфера)
                // GetRadarAltitude:      высота над реальным рельефом/океаном
                var baroAlt  = v.GetBarometricAltitude() / 1000.0;
                var radarAlt = v.GetRadarAltitude()       / 1000.0;
                sb.AppendLine($"  Alt (sphere):   {baroAlt:F1} km");
                sb.AppendLine($"  Alt (terrain):  {radarAlt:F1} km");

                // Масса и топливо
                sb.AppendLine($"  Propellant:     {(double)v.PropellantMass:F0} kg");
                sb.AppendLine($"  Total mass:     {(double)v.TotalMass:F0} kg");
                sb.AppendLine($"  Delta-V:        {v.NavBallData.DeltaVInVacuum:F0} m/s");

                // TWR на текущем дросселе
                sb.AppendLine($"  TWR:            {v.NavBallData.ThrustWeightRatio:F2}");

                // Ориентация: Roll / Pitch / Yaw (в градусах, в текущей системе отсчёта навбола)
                var att = v.NavBallData.AttitudeAngles;
                sb.AppendLine($"  Roll/Pitch/Yaw: {att.X}° / {att.Y}° / {att.Z}° (frame: {v.NavBallData.Frame})");

                // Ситуация и регион
                sb.AppendLine($"  Situation:      {v.Situation}");
                sb.AppendLine($"  Region:         {v.VehicleRegion}");

                // Расстояние до SOI (если тело Celestial)
                if (v.Orbit.Parent is Celestial parentCel &&
                    double.IsFinite(parentCel.SphereOfInfluence) &&
                    parentCel.SphereOfInfluence > 0)
                {
                    var distFromCenter = v.Orbit.StateVectors.PositionCci.Length();
                    var distToSoiEdge  = (parentCel.SphereOfInfluence - distFromCenter) / 1000.0;
                    sb.AppendLine($"  Dist to SOI edge: {distToSoiEdge:F0} km");
                }
            }
            else
            {
                sb.AppendLine("  Not in flight or orbital data unavailable");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Telemetry error: {ex.Message}");
            AdvisorMod.Log($"Telemetry error: {ex.Message}");
        }

        sb.AppendLine();
        sb.Append($"Question: {question}");
        return sb.ToString();
    }

    // ── Вспомогательные методы ─────────────────────────────────────────────

    private int GetBodyCount()
    {
        try { return Universe.CurrentSystem?.All.Count ?? 0; }
        catch { return 0; }
    }

    private static double ToDouble(object? val) =>
        val is IConvertible c ? c.ToDouble(CultureInfo.InvariantCulture) : 0.0;

    private static string FmtDist(double m)
    {
        var au = m / 1.496e11;
        return au > 0.1 ? $"{au:F3} AU" : $"{m / 1e6:F0}k km";
    }

    private static string FmtPeriod(double s)
    {
        var d = s / 86400.0;
        return d > 365 ? $"{d / 365.25:F2} years" : $"{d:F1} days";
    }

    private const string DefaultPrompt = """
        You are a mission advisor. Specialization: orbital mechanics,
        maneuver planning, dV budgets. Direct and confident, like an
        experienced flight director. Dry humor is welcome when the
        situation calls for it.

        CONTEXT
        Kitten Space Agency is a realistic space game with real orbital
        mechanics. Players design spacecraft and plan missions: orbits,
        interplanetary transfers, rendezvous, return flights.
        The audience knows the basics: apoapsis, Hohmann transfer, TWR.
        Skip the tutorials - answer with specific numbers and actions.

        PHYSICS AND DATA
        Physical laws are real: Newtonian mechanics, Tsiolkovsky equation,
        Kepler's laws. Apply them freely in calculations.

        Everything specific about system objects - masses, radii, orbits,
        current positions - take only from the session context. The system
        may be entirely fictional: different planets, different distances,
        different physical properties.

        No parameter in context -> "the game does not provide this data"
        Parameter available -> cite the source: "according to game data,
        Mars is at anomaly 87"
        Never substitute real astronomical data for game data.

        FORMAT
        - Conclusion first, numbers after
        - 4-6 lines maximum
        - No markdown, tables, or headers
        - Always check dV before advising on a maneuver
        - "Is it possible" -> yes/no with reasoning, no hedging

        Always respond in the language the user wrote in.
        """;
}
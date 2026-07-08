using System.Globalization;
using System.Reflection;
using System.Text;
using KSA;

namespace KSAAdvisor;

public class GameStateReader
{
    private readonly Config _config;
    private string? _cachedSystemPrompt;
    private int     _cachedBodyCount = -1;
    private string? _cachedSkillLevel;

    public GameStateReader(Config config)
    {
        _config = config;
    }

    private static Vehicle? GetVehicle()
    {
        var field = typeof(Program).GetField(
            "ControlledVehicle",
            BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null) as Vehicle;
    }

    private static IEnumerable<Astronomical> GetCelestialBodies()
    {
        var system = Universe.CurrentSystem;
        if (system == null) yield break;
        foreach (var astro in system.All.AsSpan().ToArray())
        {
            if (astro is Vehicle || astro is KittenEva) continue;
            yield return astro;
        }
    }

    public string BuildStaticSystemPrompt()
    {
        var personaPath = Path.Combine(Config.ModDir, "persona.txt");
        var persona = File.Exists(personaPath)
            ? File.ReadAllText(personaPath, Encoding.UTF8)
            : DefaultPersona;

        int bodyCount = GetBodyCount();
        if (_cachedSystemPrompt != null &&
            bodyCount == _cachedBodyCount &&
            _config.UserSkillLevel == _cachedSkillLevel)
            return _cachedSystemPrompt;

        _cachedBodyCount  = bodyCount;
        _cachedSkillLevel = _config.UserSkillLevel;

        var sb = new StringBuilder();
        sb.AppendLine(persona.Trim());
        sb.AppendLine();
        sb.AppendLine(CoreInstructions);
        sb.AppendLine();
        sb.AppendLine(GetSkillLevelInstruction());

        try
        {
            var bodies = GetCelestialBodies()
                .Where(a => a is IOrbiter orb && orb.Orbit != null)
                .Select(a => a.Id)
                .ToList();

            if (bodies.Count > 0)
            {
                sb.AppendLine($"BODIES IN SYSTEM: {string.Join(", ", bodies)}");
                sb.AppendLine("Use get_body_info(name) for orbital elements, mass, SOI, atmosphere.");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[Error reading system: {ex.Message}]");
        }

        _cachedSystemPrompt = sb.ToString();
        return _cachedSystemPrompt;
    }

    public string BuildDynamicUserMessage(string question)
    {
        try
        {
            var elapsed = Universe.GetElapsedSimTime();
            return $"Game time: {elapsed.Seconds() / 86400.0:F1} days.\n\n{question}";
        }
        catch { return question; }
    }

    public string GetVesselTelemetry()
    {
        try
        {
            var v = GetVehicle();
            if (v?.Orbit == null) return "No vessel in flight.";

            var sb = new StringBuilder();
            var r    = ToDouble(v.Orbit.Parent.MeanRadius);
            var apo  = (ToDouble(v.Orbit.Apoapsis)  - r) / 1000.0;
            var peri = (ToDouble(v.Orbit.Periapsis) - r) / 1000.0;

            sb.AppendLine($"Orbiting:       {v.Orbit.Parent?.Id ?? "?"}");
            sb.AppendLine($"Apoapsis:       {apo:F0} km");
            sb.AppendLine($"Periapsis:      {peri:F0} km");
            sb.AppendLine($"Orbital speed:  {v.OrbitalSpeed:F0} m/s");
            sb.AppendLine($"Surface speed:  {v.GetSurfaceSpeed():F0} m/s");
            sb.AppendLine($"Alt (sphere):   {v.GetBarometricAltitude() / 1000.0:F1} km");
            sb.AppendLine($"Alt (terrain):  {v.GetRadarAltitude() / 1000.0:F1} km");
            sb.AppendLine($"Propellant:     {(double)v.PropellantMass:F0} kg");
            sb.AppendLine($"Total mass:     {(double)v.TotalMass:F0} kg");
            sb.AppendLine($"Delta-V:        {v.NavBallData.DeltaVInVacuum:F0} m/s");
            sb.AppendLine($"TWR:            {v.NavBallData.ThrustWeightRatio:F2}");
            sb.AppendLine($"Situation:      {v.Situation}");
            sb.AppendLine($"Region:         {v.VehicleRegion}");
            var att = v.NavBallData.AttitudeAngles;
            sb.AppendLine($"Roll/Pitch/Yaw: {att.X}° / {att.Y}° / {att.Z}° ({v.NavBallData.Frame})");

            if (v.Orbit.Parent is Celestial pc &&
                double.IsFinite(pc.SphereOfInfluence) && pc.SphereOfInfluence > 0)
            {
                var dist = (pc.SphereOfInfluence - v.Orbit.StateVectors.PositionCci.Length()) / 1000.0;
                sb.AppendLine($"Dist to SOI:    {dist:F0} km");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"GetVesselTelemetry error: {ex.Message}");
            return $"Telemetry error: {ex.Message}";
        }
    }

    public string GetBodyInfo(string bodyName)
    {
        try
        {
            var astro = Universe.CurrentSystem?.Get(bodyName);
            if (astro == null)
                return $"Body '{bodyName}' not found in current system.";

            var sb = new StringBuilder();
            sb.AppendLine($"{astro.Id} ({astro.GetType().Name}):");

            if (astro is IOrbiter orb && orb.Orbit != null)
            {
                var apo  = ToDouble(orb.Orbit.Apoapsis);
                var peri = ToDouble(orb.Orbit.Periapsis);
                var per  = ToDouble(orb.Orbit.Period);
                sb.AppendLine($"  Orbit:  {FmtDist(peri)} - {FmtDist(apo)}");
                sb.AppendLine($"  Period: {FmtPeriod(per)}");
                sb.AppendLine($"  Around: {orb.Parent?.Id ?? "?"}");

                var ta = orb.Orbit.GetTrueAnomaly();
                if (double.IsFinite(ta.Value()))
                    sb.AppendLine($"  True anomaly: {ta.Value() * 180.0 / Math.PI:F1}°");
            }

            if (astro is Celestial cel)
            {
                sb.AppendLine($"  Radius: {cel.MeanRadius / 1000.0:F0} km");
                if (cel.Mass > 0)
                    sb.AppendLine($"  mu: {6.6743e-11 * cel.Mass:E3} m3/s2");
                if (double.IsFinite(cel.Eccentricity))
                    sb.AppendLine($"  Eccentricity: {cel.Eccentricity:F4}");
                if (double.IsFinite(cel.Inclination))
                    sb.AppendLine($"  Inclination: {cel.Inclination * 180.0 / Math.PI:F2}°");
                if (double.IsFinite(cel.LongitudeOfAscendingNode))
                    sb.AppendLine($"  LAN: {cel.LongitudeOfAscendingNode * 180.0 / Math.PI:F2}°");
                if (double.IsFinite(cel.ArgumentOfPeriapsis))
                    sb.AppendLine($"  AoP: {cel.ArgumentOfPeriapsis * 180.0 / Math.PI:F2}°");
                if (double.IsFinite(cel.SphereOfInfluence) && cel.SphereOfInfluence > 0)
                    sb.AppendLine($"  SOI: {FmtDist(cel.SphereOfInfluence)}");

                var omega = cel.GetAngularVelocity();
                if (Math.Abs(omega) > 1e-15)
                    sb.AppendLine($"  Day length: {2 * Math.PI / Math.Abs(omega) / 3600.0:F1} h");

                var atmoH = ((IParentBody)cel).GetAtmosphereReference()?.Physical.Height;
                if (atmoH != null)
                    sb.AppendLine($"  Atmosphere: {(double)atmoH / 1000.0:F0} km");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"GetBodyInfo error: {ex.Message}");
            return $"Error getting body info: {ex.Message}";
        }
    }

    public string GetTransferWindow(string targetName)
    {
        try
        {
            var v = GetVehicle();
            if (v?.Orbit == null) return "No vessel in flight.";

            var target = Universe.CurrentSystem?.Get(targetName) as Celestial;
            if (target?.Orbit == null)
                return $"Body '{targetName}' not found or has no orbit.";
            if (target.Orbit.Eccentricity >= 1.0)
                return $"'{targetName}' has no closed orbit.";
            if (target == v.Orbit.Parent)
                return $"Already orbiting {targetName}.";

            var parentCel = v.Orbit.Parent as Celestial;
            IOrbiter source = parentCel != null && !parentCel.IsStar()
                ? (IOrbiter)parentCel : (IOrbiter)v;

            var currentTime  = Universe.GetElapsedSimTime();
            var transferInfo = new OrbitalTransfers.TransferInfo(source, target, v, usePorkChopData: false);
            var alignment    = OrbitalTransfers.AlignmentTime(transferInfo, currentTime);
            var sourceOrbit  = parentCel != null && !parentCel.IsStar()
                ? parentCel.Orbit : v.Orbit;
            var hohmann      = OrbitalTransfers.HohmannFlight(sourceOrbit, target.Orbit);

            var wait    = alignment.Seconds() / 86400.0;
            var transit = hohmann.Seconds()   / 86400.0;

            return
                $"Transfer window to {targetName}:\n" +
                $"  Depart in:    {wait:F5} days (~{FmtDuration(wait)})\n" +
                $"  Transit time: {transit:F2} days (~{FmtDuration(transit)})\n" +
                $"  Arrival in:   {wait + transit:F2} days\n" +
                $"  For warp_time, use the exact decimal days value: {wait:F5}";
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"GetTransferWindow error: {ex.Message}");
            return $"Transfer window error: {ex.Message}";
        }
    }

    public string GetPlanetPositions()
    {
        try
        {
            var sb = new StringBuilder("Current true anomalies:\n");
            foreach (var astro in GetCelestialBodies())
            {
                if (astro is not IOrbiter orb || orb.Orbit == null) continue;
                var ta  = orb.Orbit.GetTrueAnomaly().Value();
                var deg = double.IsFinite(ta) ? $"{ta * 180.0 / Math.PI:F1}°" : "?";
                sb.AppendLine($"  {astro.Id}: {deg}");
            }
            return sb.ToString();
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    public string GetBurns()
    {
        try
        {
            var v = GetVehicle();
            if (v == null) return "No vessel.";

            var plan  = v.FlightComputer.BurnPlan;
            var count = plan.BurnCount;
            if (count == 0) return "No burns planned.";

            var now = Universe.GetElapsedSimTime();
            var sb  = new StringBuilder($"Planned burns ({count}):\n");

            for (int i = 0; i < count; i++)
            {
                if (!plan.TryGetBurn(i, out var burn) || burn == null) continue;

                var inDays = (burn.Time - now).Seconds() / 86400.0;

                string dvStr = "";
                try
                {
                    var prop = burn.GetType().GetProperty("DeltaVVlf",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop?.GetValue(burn) is Brutal.Numerics.double3 dv)
                        dvStr = $", dV {dv.Length():F1} m/s";
                }
                catch { }

                sb.AppendLine($"  Burn {i + 1}: in {inDays:F5} days (~{FmtDuration(inDays)}){dvStr}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"GetBurns error: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    public string GetOtherVessels()
    {
        try
        {
            var controlled = GetVehicle();
            var system     = Universe.CurrentSystem;
            if (system == null) return "No system.";

            var others = system.All.AsSpan().ToArray()
                .OfType<Vehicle>()
                .Where(v => v != controlled && v.Orbit != null)
                .ToList();

            if (others.Count == 0) return "No other vessels in system.";

            var sb = new StringBuilder($"Other vessels ({others.Count}):\n");
            foreach (var v in others)
            {
                var orbit = v.Orbit!;
                var r     = ToDouble(orbit.Parent.MeanRadius);
                var apo   = (ToDouble(orbit.Apoapsis)  - r) / 1000.0;
                var peri  = (ToDouble(orbit.Periapsis) - r) / 1000.0;
                sb.AppendLine($"  {v.Id}: {apo:F0}x{peri:F0} km around {orbit.Parent.Id}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"GetOtherVessels error: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    public string CreateCircularizationBurn(string at)
    {
        try
        {
            var v = GetVehicle();
            if (v?.Orbit == null) return "No vessel in flight.";
            if (v.Orbit.Eccentricity >= 1.0) return "Orbit is hyperbolic, cannot circularize.";

            var now      = Universe.GetElapsedSimTime();
            var burnTime = at.ToLowerInvariant().Contains("peri")
                ? v.Orbit.GetNextPeriapsisTime(now)
                : v.Orbit.GetNextApoapsisTime(now);

            var dvCci = OrbitalTransfers.DvCciToCircularize(v.Orbit, burnTime);

            var sv       = v.Orbit.GetStateVectorsAt(burnTime);
            var rotation = sv.GetVlf2ParentCci().OrIdentity().Inverse();
            var dvVlf    = dvCci.Transform(rotation);

            var patch = v.FlightPlan.TryFindPatch(burnTime);
            if (patch == null)
                return "Could not find orbital patch at burn time.";

            var point = v.Orbit.GetPointAt(burnTime);
            var burn  = Burn.Create(point, burnTime.Seconds(), dvVlf, patch, v);
            burn.IsGizmoActive = false;

            InputEvents.BurnUpdateBuffer.Add(new InputEvents.BurnUpdateData
            {
                Burn           = burn,
                FlightComputer = v.FlightComputer,
                AddBurn        = true
            });

            var inDays = (burnTime - now).Seconds() / 86400.0;

            AdvisorMod.Log($"Circularization burn created at {at}, dV={dvCci.Length():F1} m/s, in {inDays:F5} days");
            return
                $"Circularization burn created at {at}:\n" +
                $"  dV: {dvCci.Length():F1} m/s\n" +
                $"  In: {inDays:F5} days (~{FmtDuration(inDays)})\n" +
                $"  For warp_time, use the exact decimal days value: {inDays:F5}";
        }
        catch (Exception ex)
        {
            AdvisorMod.Log($"CreateCircularizationBurn error: {ex.Message}");
            return $"Error creating burn: {ex.Message}";
        }
    }

    private int GetBodyCount()
    {
        try { return Universe.CurrentSystem?.All.Count ?? 0; }
        catch { return 0; }
    }

    private string GetSkillLevelInstruction() => _config.UserSkillLevel.ToLowerInvariant() switch
    {
        "beginner" =>
            "USER SKILL LEVEL: beginner. Briefly explain unfamiliar terms " +
            "in plain language the first time you use them. Stay concise.",

        "expert" =>
            "USER SKILL LEVEL: expert. Skip all explanations of standard " +
            "concepts entirely. Use precise terminology, numbers only.",

        _ => ""
    };

    private static double ToDouble(object? val) =>
        val is IConvertible c ? c.ToDouble(CultureInfo.InvariantCulture) : 0.0;

    internal static string FmtDuration(double days)
    {
        var totalSeconds = (long)Math.Round(days * 86400.0);
        var d = totalSeconds / 86400;
        var h = (totalSeconds % 86400) / 3600;
        var m = (totalSeconds % 3600) / 60;
        var s = totalSeconds % 60;

        if (d > 0)  return $"{d}d {h}h {m}m";
        if (h > 0)  return $"{h}h {m}m";
        if (m > 0)  return $"{m}m {s}s";
        return $"{s}s";
    }

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

    private const string DefaultPersona = """
        You are a mission advisor. Specialization: orbital mechanics,
        maneuver planning, dV budgets. Direct and confident, like an
        experienced flight director. Dry humor is welcome.
        """;

    private const string CoreInstructions = """
        CONTEXT
        Kitten Space Agency - realistic space game with real orbital
        mechanics. Players design spacecraft and plan missions.
        Audience knows the basics. Skip tutorials, give specific numbers.

        PHYSICS AND DATA
        Physical laws are real. Apply them freely.
        All specific data about bodies and vessels - only from tool results.
        Never substitute real astronomical data for game data.
        If a tool does not return a value you need, say "the game does not
        provide this data" - never guess or fall back to real-world values.

        FORMAT
        - Conclusion first, numbers after
        - 4-6 lines maximum
        - No markdown
        - Always check dV before advising on a maneuver

        Always respond in the language the user wrote in.

        TIME FORMATTING
        Never show raw decimal days to the user - convert to natural units.
        When calling warp_time, always use the exact decimal days value
        from the tool result.

        MANEUVERS
        Before creating any maneuver, always call get_burns() first to check
        if a similar maneuver is already planned. Do not create duplicates.

        TIME-SENSITIVE DATA
        Telemetry and burns become stale after a time warp. Call relevant
        tools again instead of reusing old values from earlier in the chat.

        WARPING TIME
        Only call warp_time when the user has explicitly confirmed it.
        Do not warp on your own initiative.
        """;
}

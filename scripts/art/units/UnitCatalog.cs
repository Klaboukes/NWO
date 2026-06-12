using Godot;
using NWO.Art.Painterly;

namespace NWO.Art.Units;

// The 19 unit compositions (data/units.json ids): each dresses the shared
// figure (or picks a ship/vehicle/mount) and hands it its kit. Cloth colours
// are unit identity, not owner colour — the banner carries the team tint.
// Unknown ids get a painterly disc token so new content never crashes.
public static class UnitCatalog
{
    private static readonly Vector2 FootC = new(128f, HumanoidPainter.GroundY);
    private static readonly Vector2 FootShadow = new(40f, 10f);

    public static void Draw(UnitPaintContext ctx, string unitId)
    {
        switch (unitId)
        {
            case "scout":        Scout(ctx);       break;
            case "warrior":      Warrior(ctx);     break;
            case "archer":       Archer(ctx);      break;
            case "spearman":     Spearman(ctx);    break;
            case "horseman":     Horseman(ctx);    break;
            case "swordsman":    Swordsman(ctx);   break;
            case "catapult":     Catapult(ctx);    break;
            case "settler":      Settler(ctx);     break;
            case "worker":       Worker(ctx);      break;
            case "palace_guard": PalaceGuard(ctx); break;
            case "pioneer":      Pioneer(ctx);     break;
            case "legionary":    Legionary(ctx);   break;
            case "mercenary":    Mercenary(ctx);   break;
            case "drone":        Drone(ctx);       break;
            case "ranger":       Ranger(ctx);      break;
            case "galley":       Ship(ctx, ShipPainter.Galley);    break;
            case "frigate":      Ship(ctx, ShipPainter.Frigate);   break;
            case "transport":    Ship(ctx, ShipPainter.Transport); break;
            case "galleon":      Ship(ctx, ShipPainter.Galleon);   break;
            default:             DiscToken(ctx);   break;
        }
    }

    private static void Finish(UnitPaintContext ctx) => ctx.Finish(FootC, FootShadow);

    private static void Scout(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Leather,
            Legs  = MaterialRamps.Cloth(new Color(0.34f, 0.38f, 0.28f)),
            Cape  = true, CapeColor = new Color(0.24f, 0.34f, 0.22f),
            NearHand = new Vector2(162f, 118f), // raised to the eye-line
        };
        HumanoidPainter.Draw(ctx, fig, nearWeapon: h => WeaponPainter.Spyglass(ctx, h));
        Finish(ctx);
    }

    private static void Warrior(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            SkinVariant = 1,
            Torso = MaterialRamps.Cloth(new Color(0.46f, 0.30f, 0.20f)),
            NearHand = new Vector2(168f, 130f),
            FarHand  = new Vector2(88f, 146f),
        };
        HumanoidPainter.Draw(ctx, fig,
            farWeapon:  h => WeaponPainter.RoundShield(ctx, h + new Vector2(-6f, 0f), 22f,
                                                       new Color(0.50f, 0.34f, 0.20f)),
            nearWeapon: h => WeaponPainter.Club(ctx, h));
        Finish(ctx);
    }

    private static void Archer(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Leather,
            Legs  = MaterialRamps.Cloth(new Color(0.36f, 0.32f, 0.26f)),
            FarHand  = new Vector2(86f, 140f),
            NearHand = new Vector2(120f, 142f), // drawing hand at the string
        };
        HumanoidPainter.Draw(ctx, fig, farWeapon: h => WeaponPainter.Bow(ctx, h));
        Finish(ctx);
    }

    private static void Spearman(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Cloth(new Color(0.30f, 0.36f, 0.46f)),
            Helmet = MaterialRamps.Bronze,
            FarHand  = new Vector2(92f, 150f),
            NearHand = new Vector2(166f, 140f),
        };
        HumanoidPainter.Draw(ctx, fig,
            farWeapon:  h => WeaponPainter.RoundShield(ctx, h + new Vector2(-6f, 0f), 24f,
                                                       new Color(0.34f, 0.40f, 0.52f)),
            nearWeapon: h => WeaponPainter.Spear(ctx, h));
        Finish(ctx);
    }

    private static void Horseman(UnitPaintContext ctx)
    {
        AnimalPainter.HorseWithRider(ctx, MaterialRamps.Cloth(new Color(0.42f, 0.30f, 0.24f)));
        ctx.Finish(new Vector2(124f, HumanoidPainter.GroundY + 6f), new Vector2(52f, 11f));
    }

    private static void Swordsman(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Steel, TorsoSpecular = 0.55f,
            Helmet = MaterialRamps.Steel,
            Legs  = MaterialRamps.Cloth(new Color(0.32f, 0.30f, 0.34f)),
            FarHand  = new Vector2(90f, 148f),
            NearHand = new Vector2(168f, 124f),
        };
        HumanoidPainter.Draw(ctx, fig,
            farWeapon:  h => WeaponPainter.KiteShield(ctx, h + new Vector2(-4f, -2f),
                                                      new Color(0.24f, 0.34f, 0.50f)),
            nearWeapon: h => WeaponPainter.Sword(ctx, h));
        Finish(ctx);
    }

    private static void Catapult(UnitPaintContext ctx)
    {
        VehiclePainter.Catapult(ctx);
        ctx.Finish(new Vector2(120f, HumanoidPainter.GroundY - 8f), new Vector2(58f, 12f));
    }

    private static void Settler(UnitPaintContext ctx)
    {
        VehiclePainter.SettlerWagon(ctx);
        ctx.Finish(new Vector2(128f, HumanoidPainter.GroundY - 4f), new Vector2(56f, 12f));
    }

    private static void Worker(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            SkinVariant = 2,
            Torso = MaterialRamps.Cloth(new Color(0.55f, 0.45f, 0.30f)),
            Hat = true,
            NearHand = new Vector2(160f, 136f),
        };
        HumanoidPainter.Draw(ctx, fig, nearWeapon: h => WeaponPainter.Pickaxe(ctx, h));
        Finish(ctx);
    }

    private static void PalaceGuard(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Steel, TorsoSpecular = 0.6f,
            Helmet = MaterialRamps.Gold, Plume = true,
            Legs  = MaterialRamps.Cloth(new Color(0.40f, 0.12f, 0.14f)),
            Cape  = true, CapeColor = new Color(0.48f, 0.12f, 0.14f),
            NearHand = new Vector2(162f, 142f),
        };
        HumanoidPainter.Draw(ctx, fig, nearWeapon: h => WeaponPainter.Halberd(ctx, h));
        Finish(ctx);
    }

    private static void Pioneer(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            SkinVariant = 1,
            Torso = MaterialRamps.Leather,
            Legs  = MaterialRamps.Cloth(new Color(0.36f, 0.30f, 0.24f)),
            Hat = true,
            NearHand = new Vector2(162f, 146f),
        };
        // Backpack behind the torso.
        var packC = new Vector2(104f, 124f);
        ctx.Painter.FillShaded(p => Sdf.Box(p, packC, new Vector2(11f, 16f), 5f),
                               HumanoidPainter.Bounds(packC - new Vector2(16f, 22f), packC + new Vector2(16f, 22f), 4f),
                               MaterialRamps.Leather, 8f);
        HumanoidPainter.Draw(ctx, fig, nearWeapon: h => WeaponPainter.SurveyPole(ctx, h));
        Finish(ctx);
    }

    private static void Legionary(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Steel, TorsoSpecular = 0.5f,
            Helmet = MaterialRamps.Steel, Plume = true,
            Legs  = MaterialRamps.Cloth(new Color(0.55f, 0.16f, 0.12f)),
            FarHand  = new Vector2(92f, 146f),
            NearHand = new Vector2(164f, 138f),
        };
        HumanoidPainter.Draw(ctx, fig,
            farWeapon:  h => WeaponPainter.Scutum(ctx, h + new Vector2(-6f, -4f)),
            nearWeapon: h => WeaponPainter.Gladius(ctx, h));
        Finish(ctx);
    }

    private static void Mercenary(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            SkinVariant = 2,
            Torso = MaterialRamps.Leather,
            Legs  = MaterialRamps.Cloth(new Color(0.28f, 0.28f, 0.30f)),
            Hair  = new Color(0.12f, 0.10f, 0.09f),
            NearHand = new Vector2(170f, 120f), // blade drawn high
        };
        // One mismatched steel pauldron on the near shoulder.
        var pad = new Vector2(146f, 106f);
        HumanoidPainter.Draw(ctx, fig, nearWeapon: h => WeaponPainter.Sword(ctx, h, angle: 0.85f));
        ctx.Painter.FillShaded(p => Sdf.Ellipse(p, pad, new Vector2(11f, 8f)),
                               HumanoidPainter.Bounds(pad - new Vector2(15f, 12f), pad + new Vector2(15f, 12f), 4f),
                               MaterialRamps.Steel, 6f, specular: 0.5f);
        Finish(ctx);
    }

    private static void Drone(UnitPaintContext ctx)
    {
        VehiclePainter.Drone(ctx);
        // Hover shadow: small, well below the body.
        ctx.Finish(new Vector2(128f, HumanoidPainter.GroundY - 10f), new Vector2(30f, 7f),
                   shadowStrength: 0.30f);
    }

    private static void Ranger(UnitPaintContext ctx)
    {
        var fig = new HumanoidPainter.Figure
        {
            Torso = MaterialRamps.Cloth(new Color(0.28f, 0.36f, 0.24f)),
            Legs  = MaterialRamps.Cloth(new Color(0.24f, 0.28f, 0.22f)),
            Hat = true,
            NearHand = new Vector2(158f, 140f),
        };
        HumanoidPainter.Draw(ctx, fig, nearWeapon: h => WeaponPainter.Rifle(ctx, h));
        Finish(ctx);
    }

    private static void Ship(UnitPaintContext ctx, ShipPainter.ShipSpec spec)
    {
        ShipPainter.Draw(ctx, spec);
        ctx.Finish(new Vector2(128f, spec.DeckY + spec.HullDepth + 6f), new Vector2(72f, 11f),
                   shadowStrength: 0.35f);
    }

    // Painterly fallback token for unknown ids.
    private static void DiscToken(UnitPaintContext ctx)
    {
        var c = new Vector2(128f, 140f);
        ctx.Painter.FillShaded(p => Sdf.Circle(p, c, 52f),
                               HumanoidPainter.Bounds(c, c, 58f),
                               ColorRamp.Painterly(new Color(0.72f, 0.70f, 0.66f)), 30f,
                               specular: 0.25f);
        ctx.Finish(new Vector2(128f, 200f), new Vector2(46f, 10f));
    }
}

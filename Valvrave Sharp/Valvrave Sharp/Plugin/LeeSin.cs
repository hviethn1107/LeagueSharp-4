﻿namespace Valvrave_Sharp.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows.Forms;

    using LeagueSharp;
    using LeagueSharp.SDK.Core;
    using LeagueSharp.SDK.Core.Enumerations;
    using LeagueSharp.SDK.Core.Extensions;
    using LeagueSharp.SDK.Core.Extensions.SharpDX;
    using LeagueSharp.SDK.Core.UI.IMenu.Values;
    using LeagueSharp.SDK.Core.Utils;
    using LeagueSharp.SDK.Core.Wrappers;

    using SharpDX;

    using Valvrave_Sharp.Core;

    using Color = System.Drawing.Color;
    using Menu = LeagueSharp.SDK.Core.UI.IMenu.Menu;
    using Orbwalker = Valvrave_Sharp.Core.Orbwalker;
    using Rectangle = LeagueSharp.SDK.Core.Math.Polygons.Rectangle;

    internal class LeeSin : Program
    {
        #region Constants

        private const int RKickRange = 825;

        #endregion

        #region Static Fields

        private static int lastWardT;

        #endregion

        #region Constructors and Destructors

        public LeeSin()
        {
            Q = new Spell(SpellSlot.Q, 1100);
            Q2 = new Spell(SpellSlot.Q, 1300);
            W = new Spell(SpellSlot.W, 700);
            E = new Spell(SpellSlot.E, 400);
            E2 = new Spell(SpellSlot.E, 550);
            R = new Spell(SpellSlot.R, 375);
            Q.SetSkillshot(0.25f, 65, 1800, true, SkillshotType.SkillshotLine);
            W.SetTargetted(0.05f, 1400);
            R.SetTargetted(0.25f, 1500);
            Q.DamageType = Q2.DamageType = W.DamageType = R.DamageType = DamageType.Physical;
            E.DamageType = DamageType.Magical;
            Q.MinHitChance = HitChance.VeryHigh;

            Insec.Init();
            var orbwalkMenu = MainMenu.Add(new Menu("Orbwalk", "Orbwalk"));
            {
                orbwalkMenu.KeyBind("Star", "Star Combo", Keys.X);
                orbwalkMenu.Bool("E", "Use E");
                orbwalkMenu.Separator("Q Settings");
                orbwalkMenu.Bool("Q", "Use Q");
                orbwalkMenu.Bool("Q2", "-> Also Q2");
                orbwalkMenu.Bool("QCol", "Smite Collision");
                orbwalkMenu.Separator("R Settings");
                orbwalkMenu.Bool("R", "Use R");
                orbwalkMenu.Bool("RKill", "If Kill Enemy Behind");
                orbwalkMenu.Slider("RCountA", "Or Hit Enemy Behind >=", 1, 1, 4);
                orbwalkMenu.Separator("Sub Settings");
                orbwalkMenu.Bool("Ignite", "Use Ignite");
                orbwalkMenu.Bool("Item", "Use Item");
            }
            var farmMenu = MainMenu.Add(new Menu("Farm", "Farm"));
            {
                farmMenu.Bool("Q", "Use Q1");
            }
            var ksMenu = MainMenu.Add(new Menu("KillSteal", "Kill Steal"));
            {
                ksMenu.Bool("Q", "Use Q");
                ksMenu.Bool("E", "Use E");
                ksMenu.Bool("R", "Use R");
                ksMenu.Separator("Extra R Settings");
                foreach (var enemy in GameObjects.EnemyHeroes)
                {
                    ksMenu.Bool("RCast" + enemy.ChampionName, "Cast On " + enemy.ChampionName, false);
                }
            }
            var drawMenu = MainMenu.Add(new Menu("Draw", "Draw"));
            {
                drawMenu.Bool("Q", "Q Range", false);
                drawMenu.Bool("W", "W Range");
                drawMenu.Bool("E", "E Range", false);
                drawMenu.Bool("R", "R Range", false);
            }
            MainMenu.KeyBind("FleeW", "Use W To Flee", Keys.C);

            Game.OnUpdate += OnUpdate;
            Drawing.OnDraw += args =>
                {
                    if (Player.IsDead)
                    {
                        return;
                    }
                    if (MainMenu["Draw"]["Q"] && Q.Level > 0)
                    {
                        Drawing.DrawCircle(
                            Player.Position,
                            (IsQOne ? Q : Q2).Range,
                            Q.IsReady() ? Color.LimeGreen : Color.IndianRed);
                    }
                    if (MainMenu["Draw"]["W"] && W.Level > 0 && IsWOne)
                    {
                        Drawing.DrawCircle(Player.Position, W.Range, W.IsReady() ? Color.LimeGreen : Color.IndianRed);
                    }
                    if (MainMenu["Draw"]["E"] && E.Level > 0)
                    {
                        Drawing.DrawCircle(
                            Player.Position,
                            (IsEOne ? E : E2).Range,
                            E.IsReady() ? Color.LimeGreen : Color.IndianRed);
                    }
                    if (MainMenu["Draw"]["R"] && R.Level > 0)
                    {
                        Drawing.DrawCircle(Player.Position, R.Range, R.IsReady() ? Color.LimeGreen : Color.IndianRed);
                    }
                };
            Obj_AI_Base.OnProcessSpellCast += (sender, args) =>
                {
                    if (!sender.IsMe)
                    {
                        return;
                    }
                    if (MainMenu["Orbwalk"]["Star"].GetValue<MenuKeyBind>().Active
                        && args.SData.Name == "BlindMonkRKick" && E.IsReady() && IsEOne
                        && HaveQ((Obj_AI_Hero)args.Target) && Player.Mana >= 80)
                    {
                        DelayAction.Add(R.Delay * 1000, () => E.Cast());
                    }
                };
        }

        #endregion

        #region Properties

        private static bool CanCastInOrbwalk
        {
            get
            {
                return (!MainMenu["Orbwalk"]["Q"] || Variables.TickCount - Q.LastCastAttemptT >= 250)
                       && (!MainMenu["Orbwalk"]["E"] || Variables.TickCount - E.LastCastAttemptT >= 250);
            }
        }

        private static bool CanR
        {
            get
            {
                return !R.IsReady() && Variables.TickCount - R.LastCastAttemptT >= 280
                       && Variables.TickCount - R.LastCastAttemptT < 1500;
            }
        }

        private static Obj_AI_Base GetQObj
        {
            get
            {
                var obj = new List<Obj_AI_Base>();
                obj.AddRange(GameObjects.EnemyHeroes);
                obj.AddRange(GameObjects.Jungle);
                obj.AddRange(GameObjects.EnemyMinions.Where(i => i.IsMinion()));
                return obj.FirstOrDefault(i => i.IsValidTarget(Q2.Range) && HaveQ(i));
            }
        }

        private static int GetSmiteDmg
        {
            get
            {
                return
                    new[] { 390, 410, 430, 450, 480, 510, 540, 570, 600, 640, 680, 720, 760, 800, 850, 900, 950, 1000 }[
                        Player.Level - 1];
            }
        }

        private static bool IsEOne
        {
            get
            {
                return E.Instance.SData.Name.ToLower().Contains("one");
            }
        }

        private static bool IsQOne
        {
            get
            {
                return Q.Instance.SData.Name.ToLower().Contains("one");
            }
        }

        private static bool IsWOne
        {
            get
            {
                return W.Instance.SData.Name.ToLower().Contains("one");
            }
        }

        private static int Passive
        {
            get
            {
                return Player.GetBuffCount("BlindMonkFlurry");
            }
        }

        #endregion

        #region Methods

        private static bool CanE2(Obj_AI_Base target)
        {
            var buff = target.GetBuff("BlindMonkTempest");
            return buff != null && buff.EndTime - Game.Time < 0.15;
        }

        private static bool CanQ2(Obj_AI_Base target)
        {
            var buff = target.GetBuff("BlindMonkSonicWave");
            return buff != null && buff.EndTime - Game.Time < 0.15;
        }

        private static void Farm()
        {
            if (!MainMenu["Farm"]["Q"] || !Q.IsReady() || !IsQOne)
            {
                return;
            }
            var minion =
                GameObjects.EnemyMinions.Where(
                    i =>
                    i.IsValidTarget(Q.Range) && i.IsMinion()
                    && Q.GetHealthPrediction(i) + i.PhysicalShield <= GetQDmg(i)
                    && (!i.InAutoAttackRange()
                            ? Q.GetHealthPrediction(i) > 0
                            : i.Health + i.PhysicalShield > Player.GetAutoAttackDamage(i, true)))
                    .OrderByDescending(i => i.MaxHealth)
                    .Select(i => Q.VPrediction(i))
                    .FirstOrDefault(i => i.Hitchance >= Q.MinHitChance);
            if (minion != null)
            {
                Q.Cast(minion.CastPosition);
            }
        }

        private static void Flee(Vector3 pos, bool isStar = false)
        {
            if (!W.IsReady() || !IsWOne || Variables.TickCount - W.LastCastAttemptT <= 500)
            {
                return;
            }
            var posJump = Player.ServerPosition.Extend(pos, Math.Min(W.Range, Player.Distance(pos)));
            var objNear = new List<Obj_AI_Base>();
            objNear.AddRange(GameObjects.AllyHeroes.Where(i => !i.IsMe));
            objNear.AddRange(
                GameObjects.AllyMinions.Where(
                    i =>
                    i.IsMinion() || i.CharData.BaseSkinName == "jarvanivstandard"
                    || i.CharData.BaseSkinName == "teemomushroom"));
            objNear.AddRange(GameObjects.AllyWards);
            var objJump =
                objNear.Where(
                    i => i.IsValidTarget(W.Range, false) && i.Distance(posJump) < (isStar ? R.Range - 50 : 250))
                    .MinOrDefault(i => i.Distance(posJump));
            if (objJump != null)
            {
                W.CastOnUnit(objJump);
            }
            else if (Variables.TickCount - lastWardT >= 1000)
            {
                var ward = Items.GetWardSlot();
                if (ward == null)
                {
                    return;
                }
                var posPlace = Player.ServerPosition.Extend(pos, Math.Min(590, Player.Distance(pos)));
                lastWardT = Variables.TickCount;
                Player.Spellbook.CastSpell(ward.SpellSlot, posPlace);
            }
        }

        private static double GetEDmg(Obj_AI_Base target)
        {
            return Player.CalculateDamage(
                target,
                DamageType.Magical,
                new[] { 60, 95, 130, 165, 200 }[E.Level - 1] + Player.FlatPhysicalDamageMod);
        }

        private static double GetQ2Dmg(Obj_AI_Base target, double subHp = 0)
        {
            var dmg = new[] { 50, 80, 110, 140, 170 }[Q.Level - 1] + 0.9 * Player.FlatPhysicalDamageMod
                      + 0.08 * (target.MaxHealth - (target.Health - subHp));
            return Player.CalculateDamage(
                target,
                DamageType.Physical,
                target is Obj_AI_Minion ? Math.Min(dmg, 400) : dmg) + subHp;
        }

        private static double GetQDmg(Obj_AI_Base target)
        {
            return Player.CalculateDamage(
                target,
                DamageType.Physical,
                new[] { 50, 80, 110, 140, 170 }[Q.Level - 1] + 0.9 * Player.FlatPhysicalDamageMod);
        }

        private static double GetRDmg(Obj_AI_Base target)
        {
            return Player.CalculateDamage(
                target,
                DamageType.Physical,
                new[] { 200, 400, 600 }[R.Level - 1] + 2 * Player.FlatPhysicalDamageMod);
        }

        private static bool HaveE(Obj_AI_Base target)
        {
            return target.HasBuff("BlindMonkTempest");
        }

        private static bool HaveQ(Obj_AI_Base target)
        {
            return target.HasBuff("BlindMonkSonicWave");
        }

        private static void KillSteal()
        {
            if (MainMenu["KillSteal"]["Q"] && Q.IsReady())
            {
                if (IsQOne)
                {
                    var targets =
                        GameObjects.EnemyHeroes.Where(
                            i =>
                            i.IsValidTarget(Q.Range)
                            && (i.Health + i.PhysicalShield <= GetQDmg(i)
                                || (i.Health + i.PhysicalShield <= GetQ2Dmg(i, GetQDmg(i))
                                    && Player.Mana - Q.Instance.ManaCost >= 30))).ToList();
                    if (targets.Count > 0
                        && targets.Select(i => Q.VPrediction(i))
                               .Where(i => i.Hitchance >= Q.MinHitChance)
                               .Any(i => Q.Cast(i.CastPosition)))
                    {
                        return;
                    }
                }
                else
                {
                    var target =
                        GameObjects.EnemyHeroes.FirstOrDefault(
                            i => i.IsValidTarget(Q2.Range) && HaveQ(i) && i.Health + i.PhysicalShield <= GetQ2Dmg(i));
                    if (target != null && Q.Cast())
                    {
                        return;
                    }
                }
                if (MainMenu["KillSteal"]["E"] && E.IsReady() && IsEOne)
                {
                    var target = E.GetTarget();
                    if (target != null && target.Health + target.MagicalShield <= GetEDmg(target) && E.Cast())
                    {
                        return;
                    }
                }
                if (MainMenu["KillSteal"]["R"] && R.IsReady())
                {
                    var target =
                        TargetSelector.GetTarget(
                            GameObjects.EnemyHeroes.Where(
                                i =>
                                i.IsValidTarget(R.Range) && MainMenu["KillSteal"]["RCast" + i.ChampionName]
                                && i.Health + i.PhysicalShield <= GetRDmg(i)),
                            R.DamageType);
                    if (target != null)
                    {
                        R.CastOnUnit(target);
                    }
                }
            }
        }

        private static void OnUpdate(EventArgs args)
        {
            if (Player.IsDead || MenuGUI.IsChatOpen || MenuGUI.IsShopOpen || Player.IsRecalling())
            {
                return;
            }
            KillSteal();
            switch (Orbwalker.ActiveMode)
            {
                case OrbwalkerMode.Orbwalk:
                    Orbwalk();
                    break;
                case OrbwalkerMode.LastHit:
                    Farm();
                    break;
                case OrbwalkerMode.None:
                    if (MainMenu["FleeW"].GetValue<MenuKeyBind>().Active)
                    {
                        Orbwalker.MoveOrder(Game.CursorPos);
                        Flee(Game.CursorPos);
                    }
                    else if (MainMenu["Orbwalk"]["Star"].GetValue<MenuKeyBind>().Active)
                    {
                        Star();
                    }
                    else if (MainMenu["Insec"]["Normal"].GetValue<MenuKeyBind>().Active)
                    {
                        Orbwalker.MoveOrder(Game.CursorPos);
                        if (Insec.NormalReady)
                        {
                            Insec.DoNormal();
                        }
                    }
                    else if (MainMenu["Insec"]["Advanced"].GetValue<MenuKeyBind>().Active)
                    {
                        Orbwalker.MoveOrder(Game.CursorPos);
                        if (Insec.AdvancedReady)
                        {
                            Insec.DoAdvanced();
                        }
                    }
                    break;
            }
        }

        private static void Orbwalk()
        {
            if (MainMenu["Orbwalk"]["R"] && R.IsReady())
            {
                if (MainMenu["Orbwalk"]["Q"] && Q.IsReady() && !IsQOne)
                {
                    var target =
                        GameObjects.EnemyHeroes.Where(i => i.IsValidTarget(R.Range) && HaveQ(i))
                            .FirstOrDefault(
                                i =>
                                i.Health + i.PhysicalShield
                                <= GetQ2Dmg(i, GetRDmg(i)) + Player.GetAutoAttackDamage(i, true));
                    if (target != null && R.CastOnUnit(target))
                    {
                        return;
                    }
                }
                foreach (var hero in
                    GameObjects.EnemyHeroes.Where(
                        i => i.IsValidTarget(R.Range) && i.Health + i.PhysicalShield > GetRDmg(i))
                        .OrderBy(i => i.Distance(Player)))
                {
                    var rect = new Rectangle(
                        hero.ServerPosition,
                        hero.ServerPosition.Extend(Player.ServerPosition, -RKickRange),
                        hero.BoundingRadius * 2);
                    var heroBehind =
                        (from behind in
                             GameObjects.EnemyHeroes.Where(
                                 i =>
                                 i.IsValidTarget(RKickRange, true, hero.ServerPosition) && i.NetworkId != hero.NetworkId)
                         let predPos = Prediction.GetPrediction(behind, R.Delay, 1, R.Speed).UnitPosition
                         where rect.IsInside(predPos)
                         select behind).ToList();
                    if (heroBehind.Count == 0)
                    {
                        break;
                    }
                    if (MainMenu["Orbwalk"]["RKill"] && heroBehind.Any(i => i.Health + i.PhysicalShield <= GetRDmg(i))
                        && R.CastOnUnit(hero))
                    {
                        return;
                    }
                    if (heroBehind.Count >= MainMenu["Orbwalk"]["RCountA"] && R.CastOnUnit(hero))
                    {
                        return;
                    }
                }
            }
            if (MainMenu["Orbwalk"]["Q"] && Q.IsReady() && CanCastInOrbwalk)
            {
                if (IsQOne)
                {
                    var target = Q.GetTarget();
                    if (target != null)
                    {
                        var pred = Q.VPrediction(target);
                        if (pred.Hitchance == HitChance.Collision)
                        {
                            if (MainMenu["Orbwalk"]["QCol"] && Smite.IsReady()
                                && pred.CollisionObjects.All(i => i.NetworkId != Player.NetworkId))
                            {
                                var col = pred.CollisionObjects.Cast<Obj_AI_Minion>().ToList();
                                if (col.Count == 1
                                    && col.Any(i => i.Health <= GetSmiteDmg && Player.Distance(i) < SmiteRange)
                                    && Player.Spellbook.CastSpell(Smite, col.First()))
                                {
                                    Q.Cast(pred.CastPosition);
                                    return;
                                }
                            }
                        }
                        else if (pred.Hitchance >= Q.MinHitChance && Q.Cast(pred.CastPosition))
                        {
                            return;
                        }
                    }
                }
                else if (MainMenu["Orbwalk"]["Q2"])
                {
                    var target = GameObjects.EnemyHeroes.FirstOrDefault(i => i.IsValidTarget(Q2.Range) && HaveQ(i));
                    if (target != null)
                    {
                        if ((CanQ2(target) || (!target.InAutoAttackRange() && CanR)
                             || target.Health + target.PhysicalShield
                             <= GetQ2Dmg(target) + Player.GetAutoAttackDamage(target, true)
                             || Player.Distance(target) > target.GetRealAutoAttackRange() + 100 || Passive == -1)
                            && Q.Cast())
                        {
                            return;
                        }
                    }
                    else if (GetQObj != null)
                    {
                        var q2Target = Q2.GetTarget();
                        if (q2Target != null && GetQObj.Distance(q2Target) < Player.Distance(q2Target)
                            && GetQObj.Distance(q2Target) < E.Range && Q.Cast())
                        {
                            return;
                        }
                    }
                }
            }
            if (MainMenu["Orbwalk"]["E"] && E.IsReady() && CanCastInOrbwalk)
            {
                if (IsEOne)
                {
                    if (E.GetTarget() != null && Player.Mana >= 70 && E.Cast())
                    {
                        return;
                    }
                }
                else if (
                    GameObjects.EnemyHeroes.Where(i => i.IsValidTarget(E2.Range) && HaveE(i))
                        .Any(i => CanE2(i) || Player.Distance(i) > i.GetRealAutoAttackRange() + 50)
                    || Passive == -1 && E.Cast())
                {
                    return;
                }
            }
            var subTarget = W.GetTarget();
            if (MainMenu["Orbwalk"]["Item"])
            {
                UseItem(subTarget);
            }
            if (subTarget == null)
            {
                return;
            }
            if (MainMenu["Orbwalk"]["Ignite"] && Ignite.IsReady() && subTarget.HealthPercent < 30
                && Player.Distance(subTarget) <= IgniteRange)
            {
                Player.Spellbook.CastSpell(Ignite, subTarget);
            }
        }

        private static void Star()
        {
            var target = (IsQOne ? Q : Q2).GetTarget();
            Orbwalker.Orbwalk(target.InAutoAttackRange() ? target : null);
            if (target == null)
            {
                return;
            }
            if (Q.IsReady())
            {
                if (IsQOne)
                {
                    if (Q.Casting(target) == CastStates.SuccessfullyCasted)
                    {
                        return;
                    }
                }
                else if (HaveQ(target)
                         && (target.Health + target.PhysicalShield
                             <= GetQ2Dmg(target) + Player.GetAutoAttackDamage(target, true)
                             || (!target.InAutoAttackRange() && CanR)) && Q.Cast())
                {
                    return;
                }
            }
            if (!R.IsReady() || !Q.IsReady() || IsQOne || !HaveQ(target))
            {
                return;
            }
            if (R.IsInRange(target))
            {
                R.CastOnUnit(target);
            }
            else if (Player.Distance(target) <= W.Range + R.Range - 100 && Player.Mana >= 60)
            {
                Flee(Prediction.GetPrediction(target, W.Delay, 1, W.Speed).UnitPosition, true);
            }
        }

        private static void UseItem(Obj_AI_Hero target)
        {
            if (target != null && (target.HealthPercent < 40 || Player.HealthPercent < 50))
            {
                if (Bilgewater.IsReady)
                {
                    Bilgewater.Cast(target);
                }
                if (BotRuinedKing.IsReady)
                {
                    BotRuinedKing.Cast(target);
                }
            }
            if (Youmuu.IsReady && Player.CountEnemy(W.Range) > 0)
            {
                Youmuu.Cast();
            }
            if (Tiamat.IsReady && Player.CountEnemy(Tiamat.Range) > 0)
            {
                Tiamat.Cast();
            }
            if (Hydra.IsReady && Player.CountEnemy(Hydra.Range) > 0)
            {
                Hydra.Cast();
            }
            if (Titanic.IsReady && Player.CountEnemy(Player.GetRealAutoAttackRange()) > 0)
            {
                Titanic.Cast();
            }
        }

        #endregion

        internal class Insec
        {
            #region Static Fields

            private static Vector3 insecFlashPos = Vector3.Zero;

            private static Vector3 insecPos = Vector3.Zero;

            private static Obj_AI_Hero insecTarget;

            private static Vector3 insecWardPos = Vector3.Zero;

            private static int lastFlash;

            private static int lastWardPlace, lastWardJump;

            #endregion

            #region Properties

            internal static bool AdvancedReady
            {
                get
                {
                    return Flash.IsReady() && R.IsReady() && insecTarget != null;
                }
            }

            internal static bool NormalReady
            {
                get
                {
                    return (CanWardJump || (MainMenu["Insec"]["Flash"] && Flash.IsReady()) || RecentInsec)
                           && R.IsReady() && insecTarget != null;
                }
            }

            private static bool CanWardJump
            {
                get
                {
                    return Items.GetWardSlot() != null && W.IsReady() && IsWOne
                           && Variables.TickCount - lastWardJump > 500;
                }
            }

            private static float DistBehind
            {
                get
                {
                    return
                        Math.Min(
                            (Player.BoundingRadius + insecTarget.BoundingRadius + 60)
                            * (MainMenu["Insec"]["Dist"] + 100) / 100,
                            R.Range);
                }
            }

            private static Vector3 PosAfterInsec
            {
                get
                {
                    return insecTarget.ServerPosition.Extend(PosInsecTo, RKickRange);
                }
            }

            private static Vector3 PosInsecTo
            {
                get
                {
                    var pos = Vector3.Zero;
                    switch (MainMenu["Insec"]["Mode"].GetValue<MenuList>().Index)
                    {
                        case 0:
                            var hero =
                                GameObjects.AllyHeroes.Where(
                                    i =>
                                    i.IsValidTarget(RKickRange + 500, false, insecTarget.ServerPosition) && !i.IsMe
                                    && i.HealthPercent > 10 && i.Distance(insecTarget) > 400)
                                    .OrderByDescending(i => i.Health)
                                    .ThenBy(i => i.Distance(insecTarget))
                                    .FirstOrDefault();
                            var turret =
                                GameObjects.AllyTurrets.Where(
                                    i =>
                                    i.Health > 0 && i.Distance(Player) < 3000
                                    && i.Distance(insecTarget) - RKickRange < i.AttackRange + 200
                                    && i.Distance(insecTarget) > 400).MinOrDefault(i => i.Distance(insecTarget));
                            if (turret != null)
                            {
                                pos = turret.ServerPosition;
                            }
                            if (!pos.IsValid() && hero != null)
                            {
                                pos = hero.ServerPosition
                                      + (insecTarget.ServerPosition - hero.ServerPosition).Normalized().Perpendicular()
                                      * (hero.AttackRange + hero.BoundingRadius + insecTarget.BoundingRadius) / 2;
                            }
                            if (!pos.IsValid())
                            {
                                pos = Player.ServerPosition;
                            }
                            break;
                        case 1:
                            pos = Game.CursorPos;
                            break;
                        case 2:
                            pos = Player.ServerPosition;
                            break;
                    }
                    return insecPos.IsValid() ? insecPos : pos;
                }
            }

            private static bool RecentInsec
            {
                get
                {
                    return (!IsWOne && Variables.TickCount - lastWardJump < 5000)
                           || Variables.TickCount - lastWardPlace < 5000
                           || (MainMenu["Insec"]["Flash"] && Variables.TickCount - lastFlash < 5000);
                }
            }

            #endregion

            #region Methods

            internal static void DoAdvanced()
            {
                if (R.IsInRange(insecTarget))
                {
                    var posBehind = insecTarget.ServerPosition.Extend(
                        PosInsecTo,
                        -(Math.Min(Player.Distance(insecTarget) + 80, FlashRange)));
                    LockInsecPos(PosAfterInsec);
                    insecFlashPos = posBehind;
                    R.CastOnUnit(insecTarget);
                }
                CastQ(true);
            }

            internal static void DoNormal()
            {
                if (R.IsInRange(insecTarget) && Player.Distance(PosInsecTo) > insecTarget.Distance(PosInsecTo))
                {
                    var project =
                        insecTarget.ServerPosition.Extend(Player.ServerPosition, -RKickRange)
                            .ProjectOn(
                                insecTarget.ServerPosition,
                                PosInsecTo.Extend(insecTarget.ServerPosition, -(R.Range * 0.5f)));
                    if (project.IsOnSegment && project.SegmentPoint.Distance(PosInsecTo) <= RKickRange * 0.5f)
                    {
                        R.CastOnUnit(insecTarget);
                    }
                }
                if (Player.Distance(insecTarget) < 600 - DistBehind && !RecentInsec)
                {
                    if (MainMenu["Insec"]["PriorFlash"])
                    {
                        if (MainMenu["Insec"]["Flash"] && Flash.IsReady())
                        {
                            GapClose(true);
                        }
                        else if (CanWardJump)
                        {
                            GapClose();
                        }
                    }
                    else
                    {
                        if (CanWardJump)
                        {
                            GapClose();
                        }
                        else if (MainMenu["Insec"]["Flash"] && Flash.IsReady())
                        {
                            GapClose(true);
                        }
                    }
                }
                CastQ();
            }

            internal static void Init()
            {
                var insecMenu = MainMenu.Add(new Menu("Insec", "Insec"));
                {
                    insecMenu.Bool("Line", "Draw Line");
                    insecMenu.List("Mode", "Mode", new[] { "Tower/Hero", "Mouse Position", "Current Position" });
                    insecMenu.Separator("Q Settings");
                    insecMenu.Bool("Q", "Use Q");
                    insecMenu.Bool("QCol", "Smite Collision");
                    insecMenu.Separator("Normal Insec Settings");
                    insecMenu.Slider("Dist", "Extra Distance Behind (%)", 20);
                    insecMenu.Bool("Flash", "Use Flash If Can't WardJump");
                    insecMenu.Bool("PriorFlash", "Priorize Flash Over WardJump", false);
                    insecMenu.Separator("Keybinds");
                    insecMenu.KeyBind("Normal", "Normal Insec", Keys.T);
                    insecMenu.KeyBind("Advanced", "Advanced Insec (R-Flash)", Keys.Z);
                }

                Game.OnUpdate += args =>
                    {
                        if (Player.IsDead)
                        {
                            return;
                        }
                        insecTarget = (Q.IsReady() ? Q2 : W).GetTarget();
                        if (!R.IsReady())
                        {
                            insecPos = Vector3.Zero;
                            insecFlashPos = Vector3.Zero;
                            insecWardPos = Vector3.Zero;
                        }
                    };
                Drawing.OnDraw += args =>
                    {
                        if (Player.IsDead || !MainMenu["Insec"]["Line"] || R.Level == 0 || !R.IsReady()
                            || insecTarget == null || !PosInsecTo.IsValid())
                        {
                            return;
                        }
                        Drawing.DrawCircle(insecTarget.Position, insecTarget.BoundingRadius * 1.5f, Color.BlueViolet);
                        Drawing.DrawLine(
                            Drawing.WorldToScreen(insecTarget.ServerPosition),
                            Drawing.WorldToScreen(insecTarget.ServerPosition.Extend(PosInsecTo, RKickRange)),
                            2,
                            Color.BlueViolet);
                    };
                Obj_AI_Base.OnProcessSpellCast += (sender, args) =>
                    {
                        if (!sender.IsMe)
                        {
                            return;
                        }
                        if (args.SData.Name == "summonerflash")
                        {
                            lastFlash = Variables.TickCount;
                            if ((MainMenu["Insec"]["Normal"].GetValue<MenuKeyBind>().Active
                                 || MainMenu["Insec"]["Advanced"].GetValue<MenuKeyBind>().Active)
                                && insecFlashPos.IsValid() && args.End.Distance(insecFlashPos) <= 100)
                            {
                                insecFlashPos = Vector3.Zero;
                                /*if (MainMenu["Insec"]["Normal"].GetValue<MenuKeyBind>().Active && R.IsReady())
                                {
                                    DelayAction.Add(30, () => R.CastOnUnit(insecTarget));
                                }*/
                            }
                        }
                        if (MainMenu["Insec"]["Advanced"].GetValue<MenuKeyBind>().Active
                            && args.SData.Name == "BlindMonkRKick" && Flash.IsReady() && insecFlashPos.IsValid())
                        {
                            Player.Spellbook.CastSpell(Flash, insecFlashPos);
                        }
                    };
                GameObject.OnCreate += (sender, args) =>
                    {
                        if (!MainMenu["Insec"]["Normal"].GetValue<MenuKeyBind>().Active || !NormalReady || !W.IsReady()
                            || !IsWOne)
                        {
                            return;
                        }
                        var ward = sender as Obj_AI_Minion;
                        if (ward == null || ward.IsEnemy
                            || (!ward.CharData.BaseSkinName.ToLower().Contains("ward")
                                && !ward.CharData.BaseSkinName.ToLower().Contains("trinket")) || !W.IsInRange(ward)
                            || Variables.TickCount - lastWardJump <= 500 || !insecWardPos.IsValid()
                            || ward.Distance(insecWardPos) > 100)
                        {
                            return;
                        }
                        lastWardJump = Variables.TickCount;
                        insecWardPos = Vector3.Zero;
                        W.CastOnUnit(ward);
                    };
            }

            private static void CastQ(bool isAdvanced = false)
            {
                if (!MainMenu["Orbwalk"]["Q"] || !Q.IsReady())
                {
                    return;
                }
                if (IsQOne)
                {
                    var state = Q.Casting(insecTarget);
                    switch (state)
                    {
                        case CastStates.SuccessfullyCasted:
                            return;
                        case CastStates.OutOfRange:
                        case CastStates.Collision:
                        case CastStates.LowHitChance:
                            if (state == CastStates.Collision && MainMenu["Insec"]["QCol"] && Smite.IsReady())
                            {
                                var pred = Q.VPrediction(insecTarget);
                                if (pred.CollisionObjects.All(i => i.NetworkId != Player.NetworkId))
                                {
                                    var col = pred.CollisionObjects.Cast<Obj_AI_Minion>().ToList();
                                    if (col.Count == 1
                                        && col.Any(i => i.Health <= GetSmiteDmg && Player.Distance(i) < SmiteRange)
                                        && Player.Spellbook.CastSpell(Smite, col.First()))
                                    {
                                        Q.Cast(pred.CastPosition);
                                        return;
                                    }
                                }
                            }
                            var nearObj = new List<Obj_AI_Base>();
                            nearObj.AddRange(GameObjects.EnemyHeroes.Where(i => i.NetworkId != insecTarget.NetworkId));
                            nearObj.AddRange(GameObjects.EnemyMinions.Where(i => i.IsMinion()));
                            nearObj.AddRange(GameObjects.Jungle);
                            nearObj =
                                nearObj.Where(
                                    i =>
                                    i.IsValidTarget(Q.Range) && Player.Distance(insecTarget) > i.Distance(insecTarget)
                                    && Q.GetHealthPrediction(i) + i.PhysicalShield > GetQDmg(i)
                                    && i.Distance(insecTarget) < (isAdvanced ? R.Range - 50 : 600 - DistBehind - 80))
                                    .OrderBy(i => i.Distance(insecTarget))
                                    .ToList();
                            if (nearObj.Count == 0)
                            {
                                return;
                            }
                            var bestObj =
                                nearObj.Select(i => Q.VPrediction(i))
                                    .Where(i => i.Hitchance >= Q.MinHitChance)
                                    .MaxOrDefault(i => i.Hitchance);
                            if (bestObj != null)
                            {
                                Q.Cast(bestObj.CastPosition);
                            }
                            break;
                    }
                }
                else if (GetQObj != null
                         && (isAdvanced
                                 ? Flash.IsReady()
                                 : (CanWardJump && Player.Mana >= 80) || (MainMenu["Insec"]["Flash"] && Flash.IsReady()))
                         && Player.Distance(insecTarget) > (isAdvanced ? R.Range : 600 - DistBehind)
                         && GetQObj.Distance(insecTarget) < (isAdvanced ? R.Range - 50 : 600 - DistBehind - 80))
                {
                    Q.Cast();
                }
            }

            private static void GapClose(bool useFlash = false)
            {
                if (useFlash)
                {
                    var posBehind = insecTarget.ServerPosition.Extend(PosInsecTo, -DistBehind);
                    var posFlash = Player.ServerPosition.Extend(posBehind, FlashRange);
                    if (insecTarget.Distance(posBehind) > R.Range || insecTarget.Distance(posFlash) <= 50
                        || insecTarget.Distance(posFlash) >= PosInsecTo.Distance(posFlash)
                        || insecTarget.Distance(posBehind) >= PosInsecTo.Distance(posBehind))
                    {
                        return;
                    }
                    LockInsecPos(PosAfterInsec);
                    insecFlashPos = posBehind;
                    Player.Spellbook.CastSpell(Flash, posBehind);
                }
                else
                {
                    var posBehind =
                        Prediction.GetPrediction(insecTarget, W.Delay, 1, W.Speed)
                            .CastPosition.Extend(PosInsecTo, -DistBehind);
                    if (Player.Distance(posBehind) >= 600 || insecTarget.Distance(posBehind) > R.Range
                        || insecTarget.Distance(posBehind) >= PosInsecTo.Distance(posBehind))
                    {
                        return;
                    }
                    LockInsecPos(PosAfterInsec);
                    var posPlace = Player.ServerPosition.Extend(posBehind, Math.Min(600, Player.Distance(posBehind)));
                    lastWardPlace = Variables.TickCount;
                    insecWardPos = posPlace;
                    Player.Spellbook.CastSpell(Items.GetWardSlot().SpellSlot, posPlace);
                }
            }

            private static void LockInsecPos(Vector3 pos)
            {
                insecPos = pos;
                DelayAction.Add(10000, () => insecPos = Vector3.Zero);
            }

            #endregion
        }
    }
}
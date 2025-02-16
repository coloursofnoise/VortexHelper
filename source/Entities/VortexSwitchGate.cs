﻿using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections;

namespace Celeste.Mod.VortexHelper.Entities {
    [CustomEntity("VortexHelper/VortexSwitchGate")]
    public class VortexSwitchGate : Solid {
        public enum SwitchGateBehavior {
            Crush,
            Shatter
        }

        private static ParticleType P_Behind = SwitchGate.P_Behind;
        private static ParticleType P_Dust = SwitchGate.P_Dust;

        private Vector2 node;
        private bool persistent;
        public SwitchGateBehavior Behavior;

        private Color inactiveColor = Calc.HexToColor("5fcde4");
        private Color activeColor = Color.White;
        private Color finishColor = Calc.HexToColor("f141df");

        private Vector2 iconOffset;
        private Sprite icon;

        private Wiggler wiggler;
        private MTexture[,] nineSlice;
        private SoundSource openSfx;
        private string debrisPath;
        private float crushSpeed;

        public VortexSwitchGate(EntityData data, Vector2 offset)
            : this(data.Position + offset, data.Width, data.Height, data.Nodes[0] + offset, data.Bool("persistent"), data.Attr("sprite", "block"), data.Enum("behavior", SwitchGateBehavior.Crush), data.Float("crushDuration")) { }

        public VortexSwitchGate(Vector2 position, int width, int height, Vector2 node, bool persistent, string spriteName, SwitchGateBehavior behavior, float crushSpeed)
            : base(position, width, height, safe: false) {
            this.node = node;
            this.persistent = persistent;
            this.crushSpeed = Calc.Min(Calc.Max(crushSpeed, 0.5f), 2f); // 0.5 < crushSpeed < 2 so the sound doesn't get messed up.
            
            Behavior = behavior;

            debrisPath = spriteName switch {
                "mirror" => "debris/VortexHelper/disintegate/2",
                "temple" => "debris/VortexHelper/disintegate/3",
                "stars" => "debris/VortexHelper/disintegate/4",
                _ => "debris/VortexHelper/disintegate/1",
            };
            Add(icon = new Sprite(GFX.Game, "objects/switchgate/icon") {
                Rate = 0f,
                Color = inactiveColor,
                Position = iconOffset = new Vector2(width / 2f, height / 2f)
            });

            icon.Add("spin", "", 0.1f, "spin");
            icon.Play("spin");
            icon.CenterOrigin();

            Add(wiggler = Wiggler.Create(0.5f, 4f, delegate (float f) {
                icon.Scale = Vector2.One * (1f + f);
            }));

            MTexture mTexture = GFX.Game["objects/switchgate/" + spriteName];
            nineSlice = new MTexture[3, 3];
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    nineSlice[i, j] = mTexture.GetSubtexture(new Rectangle(i * 8, j * 8, 8, 8));
                }
            }

            Add(openSfx = new SoundSource());
            Add(new LightOcclude(0.5f));
        }

        public override void Awake(Scene scene) {
            base.Awake(scene);
            if (Switch.CheckLevelFlag(SceneAs<Level>())) {

                if (Behavior == SwitchGateBehavior.Crush) {
                    MoveTo(node);
                    icon.Rate = 0f;
                    icon.SetAnimationFrame(0);
                    icon.Color = finishColor;
                } else
                    RemoveSelf();

            } else {
                if (Behavior == SwitchGateBehavior.Crush)
                    Add(new Coroutine(CrushSequence(node)));
                else
                    Add(new Coroutine(ShatterSequence()));
                
            }
        }

        public override void Render() {
            float num = Collider.Width / 8f - 1f;
            float num2 = Collider.Height / 8f - 1f;
            for (int i = 0; i <= num; i++) {
                for (int j = 0; j <= num2; j++) {
                    int num3 = (i < num) ? Math.Min(i, 1) : 2;
                    int num4 = (j < num2) ? Math.Min(j, 1) : 2;
                    nineSlice[num3, num4].Draw(Position + Shake + new Vector2(i * 8, j * 8));
                }
            }
            icon.Position = iconOffset + Shake;
            icon.DrawOutline();
            base.Render();
        }

        private IEnumerator ShatterSequence() {
            Level level = SceneAs<Level>();
            while (!Switch.Check(Scene)) {
                yield return null;
            }
            if (persistent) {
                Switch.SetLevelFlag(level);
            }

            openSfx.Play(SFX.game_gen_fallblock_shake);

            yield return 0.1f;
            StartShaking(0.5f);

            while (icon.Rate < 1f) {
                icon.Color = Color.Lerp(inactiveColor, finishColor, icon.Rate);
                icon.Rate += Engine.DeltaTime * 2f;
                yield return null;
            }

            yield return 0.1f;
            for (int m = 0; m < 32; m++) {
                float num = Calc.Random.NextFloat((float)Math.PI * 2f);
                SceneAs<Level>().ParticlesFG.Emit(TouchSwitch.P_Fire, Position + iconOffset + Calc.AngleToVector(num, 4f), num);
            }

            openSfx.Stop();
            Audio.Play(SFX.game_gen_wallbreak_stone, Center);
            Audio.Play(SFX.game_gen_touchswitch_gate_finish, Center);
            level.Shake();

            for (int i = 0; i < Width / 8f; i++) {
                for (int j = 0; j < Height / 8f; j++) {
                    Debris debris = new Debris().orig_Init(Position + new Vector2(4 + i * 8, 4 + j * 8), '1').BlastFrom(Center);
                    DynData<Debris> debrisData = new DynData<Debris>(debris);
                    debrisData.Get<Image>("image").Texture = GFX.Game[debrisPath];
                    Scene.Add(debris);
                }
            }

            DestroyStaticMovers();
            RemoveSelf();
        }

        private IEnumerator CrushSequence(Vector2 node) {
            Level level = SceneAs<Level>();
            Vector2 start = Position;

            while (!Switch.Check(Scene)) {
                yield return null;
            }

            if (persistent) {
                Switch.SetLevelFlag(level);
            }

            yield return 0.1f;
            StartShaking(0.5f);
            openSfx.Play(SFX.game_gen_touchswitch_gate_open);

            while (icon.Rate < 1f) {
                icon.Color = Color.Lerp(inactiveColor, activeColor, icon.Rate);
                icon.Rate += Engine.DeltaTime * 2f;
                yield return null;
            }

            yield return 0.1f;

            int particleAt = 0;
            Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeIn, crushSpeed, start: true);
            tween.OnUpdate = delegate (Tween t) {
                MoveTo(Vector2.Lerp(start, node, t.Eased));
                if (Scene.OnInterval(0.1f)) {
                    particleAt++;
                    particleAt %= 2;
                    for (int n = 0; n < Width / 8f; n++) {
                        for (int num2 = 0; num2 < Height / 8f; num2++) {
                            if ((n + num2) % 2 == particleAt) {
                                level.ParticlesBG.Emit(P_Behind, Position + new Vector2(n * 8, num2 * 8) + Calc.Random.Range(Vector2.One * 2f, Vector2.One * 6f));
                            }
                        }
                    }
                }
            };
            Add(tween);

            yield return crushSpeed;

            bool collidable = Collidable;
            Collidable = false;

            // Particles
            if (node.X <= start.X) {
                Vector2 value = new Vector2(0f, 2f);
                for (int i = 0; i < Height / 8f; i++) {
                    Vector2 vector = new Vector2(Left - 1f, Top + 4f + i * 8);
                    Vector2 point = vector + Vector2.UnitX;
                    if (Scene.CollideCheck<Solid>(vector) && !Scene.CollideCheck<Solid>(point)) {
                        level.ParticlesFG.Emit(P_Dust, vector + value, (float)Math.PI);
                        level.ParticlesFG.Emit(P_Dust, vector - value, (float)Math.PI);
                    }
                }
            }
            if (node.X >= start.X) {
                Vector2 value2 = new Vector2(0f, 2f);
                for (int j = 0; j < Height / 8f; j++) {
                    Vector2 vector2 = new Vector2(Right + 1f, Top + 4f + j * 8);
                    Vector2 point2 = vector2 - Vector2.UnitX * 2f;
                    if (Scene.CollideCheck<Solid>(vector2) && !Scene.CollideCheck<Solid>(point2)) {
                        level.ParticlesFG.Emit(P_Dust, vector2 + value2, 0f);
                        level.ParticlesFG.Emit(P_Dust, vector2 - value2, 0f);
                    }
                }
            }
            if (node.Y <= start.Y) {
                Vector2 value3 = new Vector2(2f, 0f);
                for (int k = 0; k < Width / 8f; k++) {
                    Vector2 vector3 = new Vector2(Left + 4f + k * 8, Top - 1f);
                    Vector2 point3 = vector3 + Vector2.UnitY;
                    if (Scene.CollideCheck<Solid>(vector3) && !Scene.CollideCheck<Solid>(point3)) {
                        level.ParticlesFG.Emit(P_Dust, vector3 + value3, -(float)Math.PI / 2f);
                        level.ParticlesFG.Emit(P_Dust, vector3 - value3, -(float)Math.PI / 2f);
                    }
                }
            }
            if (node.Y >= start.Y) {
                Vector2 value4 = new Vector2(2f, 0f);
                for (int l = 0; l < Width / 8f; l++) {
                    Vector2 vector4 = new Vector2(Left + 4f + l * 8, Bottom + 1f);
                    Vector2 point4 = vector4 - Vector2.UnitY * 2f;
                    if (Scene.CollideCheck<Solid>(vector4) && !Scene.CollideCheck<Solid>(point4)) {
                        level.ParticlesFG.Emit(P_Dust, vector4 + value4, (float)Math.PI / 2f);
                        level.ParticlesFG.Emit(P_Dust, vector4 - value4, (float)Math.PI / 2f);
                    }
                }
            }

            Collidable = collidable;
            Audio.Play(SFX.game_gen_touchswitch_gate_finish, Position);
            openSfx.Stop();
            StartShaking(0.2f);
            level.Shake();

            while (icon.Rate > 0f) {
                icon.Color = Color.Lerp(activeColor, finishColor, 1f - icon.Rate);
                icon.Rate -= Engine.DeltaTime * 4f;
                yield return null;
            }
            icon.Rate = 0f;
            icon.SetAnimationFrame(0);
            wiggler.Start();

            collidable = Collidable;
            Collidable = false;
            if (!Scene.CollideCheck<Solid>(Center)) {
                for (int m = 0; m < 32; m++) {
                    float num = Calc.Random.NextFloat((float)Math.PI * 2f);
                    SceneAs<Level>().ParticlesFG.Emit(TouchSwitch.P_Fire, Position + iconOffset + Calc.AngleToVector(num, 4f), num);
                }
            }
            Collidable = collidable;
        }
    }
}

﻿using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;
using System;

namespace Celeste.Mod.VortexHelper.Entities {
    [CustomEntity("VortexHelper/BubbleWrapBlock")]
    [Tracked(false)]
    public class BubbleWrapBlock : Solid {

        private enum States {
            Idle,
            Gone
        }
        private States state = States.Idle;

        private bool canDash;
        private float respawnTime;
        private float RespawnTimer;
        private float rectEffectInflate = 0f;

        private SoundSource breakSfx;

        private MTexture[,,] nineSlice;

        private Vector2 wobbleScale = Vector2.One;
        private Wiggler wobble;

        public static ParticleType P_Respawn;

        public BubbleWrapBlock(EntityData data, Vector2 offset)
            : this(data.Position + offset, data.Width, data.Height, data.Bool("canDash"), data.Float("respawnTime")) { }

        public BubbleWrapBlock(Vector2 position, int width, int height, bool canDash, float respawnTime)
            : base(position, width, height, safe: true) {
            SurfaceSoundIndex = SurfaceIndex.Brick;

            this.canDash = canDash;
            this.respawnTime = respawnTime;

            MTexture mTexture1 = GFX.Game["objects/VortexHelper/bubbleWrapBlock/bubbleBlock"];
            MTexture mTexture2 = GFX.Game["objects/VortexHelper/bubbleWrapBlock/bubbleOutline"];

            nineSlice = new MTexture[3, 3, 2];
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    nineSlice[i, j, 0] = mTexture1.GetSubtexture(new Rectangle(i * 8, j * 8, 8, 8));
                    nineSlice[i, j, 1] = mTexture2.GetSubtexture(new Rectangle(i * 8, j * 8, 8, 8));
                }
            }

            Add(new LightOcclude());
            Add(breakSfx = new SoundSource());
            Add(wobble = Wiggler.Create(.75f, 4.2f, delegate (float v) {
                wobbleScale = new Vector2(1f + v * 0.12f, 1f - v * 0.12f);
            }));

            OnDashCollide = Dashed;
        }

        public override void Render() {
            Vector2 drawOffset = Vector2.One * 4;

            float num = Collider.Width / 8f - 1f;
            float num2 = Collider.Height / 8f - 1f;

            for (int i = 0; i <= num; i++) {
                for (int j = 0; j <= num2; j++) {
                    int num3 = (i < num) ? Math.Min(i, 1) : 2;
                    int num4 = (j < num2) ? Math.Min(j, 1) : 2;

                    Vector2 pos = Position + new Vector2(i * 8, j * 8);
                    Vector2 newPos = Center + ((pos - Center) * wobbleScale) + Shake;

                    nineSlice[num3, num4, state == States.Idle ? 0 : 1].DrawCentered(newPos + drawOffset, Color.White, wobbleScale);
                }
            }

            if (state == States.Gone) {
                DrawInflatedHollowRectangle((int)X, (int)Y, (int)Width, (int)Height, (int)rectEffectInflate, Color.White * ((3 - rectEffectInflate) / 3));
            }

            base.Render();
        }

        private void RespawnParticles() {
            Level level = SceneAs<Level>();
            for (int i = 0; i < Width; i += 4) {
                level.Particles.Emit(P_Respawn, new Vector2(X + 2f + i + Calc.Random.Range(-1, 1), Y), -(float)Math.PI / 2f);
                level.Particles.Emit(P_Respawn, new Vector2(X + 2f + i + Calc.Random.Range(-1, 1), Bottom - 1f), (float)Math.PI / 2f);
            }
            for (int j = 0; j < Height; j += 4) {
                level.Particles.Emit(P_Respawn, new Vector2(X, Y + 2f + j + Calc.Random.Range(-1, 1)), (float)Math.PI);
                level.Particles.Emit(P_Respawn, new Vector2(Right - 1f, Y + 2f + j + Calc.Random.Range(-1, 1)), 0f);
            }
        }

        private void DrawInflatedHollowRectangle(int x, int y, int width, int height, int inflate, Color color) {
            Draw.HollowRect(x - inflate, y - inflate, width + 2 * inflate, height + 2 * inflate, color);
        }

        public DashCollisionResults Dashed(Player player, Vector2 direction) {
            if (!SaveData.Instance.Assists.Invincible && player.CollideCheck<Spikes>()) {
                return DashCollisionResults.NormalCollision;
            }

            if (!canDash) {
                return DashCollisionResults.NormalCollision;
            }

            if (state == States.Gone) {
                return DashCollisionResults.Ignore;
            }

            if (player.StateMachine.State == 5) {
                player.StateMachine.State = 0;
            }

            Break();

            return DashCollisionResults.Rebound;
        }

        public void Break() {
            breakSfx.Play(SFX.game_gen_wallbreak_stone);

            for (int i = 0; i < Width / 8f; i++) {
                for (int j = 0; j < Height / 8f; j++) {
                    Debris debris = new Debris().orig_Init(Position + new Vector2(4 + i * 8, 4 + j * 8), '1').BlastFrom(Center);
                    DynData<Debris> debrisData = new DynData<Debris>(debris);
                    debrisData.Get<Image>("image").Texture = GFX.Game["debris/VortexHelper/BubbleWrapBlock"];
                    Scene.Add(debris);
                }
            }

            SceneAs<Level>().Shake(.1f);
            DisableStaticMovers();
            state = States.Gone;
            Collidable = false;
            RespawnTimer = respawnTime;
        }

        public override void Update() {
            base.Update();
            if (RespawnTimer > 0f) {
                RespawnTimer -= Engine.DeltaTime;
            }
            if (RespawnTimer <= 0f) {
                if (CheckEntitySafe()) {
                    Respawn();
                }
            }

            if (state == States.Gone) {
                rectEffectInflate = Calc.Approach(rectEffectInflate, 3, 20 * Engine.DeltaTime);
            }
        }

        private void Respawn() {
            if (!Collidable) {
                wobble.Start();
                RespawnParticles();
                rectEffectInflate = 0f;

                EnableStaticMovers();
                breakSfx.Play(SFX.game_05_redbooster_reappear);
                Collidable = true;
                state = States.Idle;
            }
        }

        private bool CheckEntitySafe() {
            foreach (Solid e in Scene.Tracker.GetEntities<Solid>()) {
                if ((e is CrushBlock || e is MoveBlock) && CollideCheck(e)) {
                    return false;
                }
            }
            return !CollideCheck<Actor>() && !CollideCheck<FallingBlock>() && !CollideCheck<AutoFallingBlock>();
        }

        public static void InitializeParticles() {
            P_Respawn = new ParticleType {
                Color = Color.Lerp(Color.Purple, Color.White, .2f),
                FadeMode = ParticleType.FadeModes.Late,
                SpeedMin = 20f,
                SpeedMax = 50f,
                SpeedMultiplier = 0.1f,
                Size = 1f,
                LifeMin = 0.4f,
                LifeMax = 0.8f,
                DirectionRange = (float)Math.PI / 6f
            };
        }
    }
}

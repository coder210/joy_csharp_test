using System;
using System.Collections.Generic;
using System.Globalization;

namespace JoyGame
{
    /// <summary>最小可玩玩法：玩家移动/射击、一种追人敌人、HUD。全部逻辑在 C# 侧。</summary>
    internal sealed class PlayScene : IScene
    {
        private const float ArenaMargin = 10f;
        private const float EnemyContactDps = 26f;
        private const int MaxEnemies = 24;

        private GameApp _app;
        private Player _player;
        private readonly List<Enemy> _enemies = new List<Enemy>();
        private readonly List<Bullet> _bullets = new List<Bullet>();

        private float _elapsed;
        private float _spawnTimer;
        private int _kills;
        private int _bulletsFired;
        private bool _finished;

        public float PlayerX => _player.X;
        public float PlayerY => _player.Y;
        public int Kills => _kills;
        public int BulletsFired => _bulletsFired;
        public int EnemyCount => _enemies.Count;
        public int Wave => 1 + _kills / 8;

        public bool EnemyHasMoved
        {
            get
            {
                foreach (Enemy e in _enemies)
                {
                    if (e.Approached(_player.X, _player.Y)) return true;
                }
                return false;
            }
        }

        public void Enter(GameApp app)
        {
            _app = app;
            _player = new Player { X = app.Width * 0.5f, Y = app.Height * 0.5f };
            _spawnTimer = app.Smoke ? 1.5f : 1.0f;
            Joy.Host.log("[scene] Play");

            if (app.Smoke)
            {
                // 冒烟：三个低血量敌人摆在正右 / 右下 / 正下，脚本的斜向射击也能命中，
                // 用来验证"输入 → 射击 → 命中 → 击杀 → HUD"整条链路。
                _enemies.Add(MakeEnemy(_player.X + 90f, _player.Y, 2f));
                _enemies.Add(MakeEnemy(_player.X + 62f, _player.Y + 62f, 2f));
                _enemies.Add(MakeEnemy(_player.X, _player.Y + 90f, 2f));
            }
        }

        public void Update(float dt)
        {
            _elapsed += dt;

            UpdatePlayer(dt);
            UpdateBullets(dt);
            UpdateEnemies(dt);
            ResolveBulletHits();
            UpdateSpawn(dt);

            if (_player.Hp <= 0f && !_finished)
            {
                _finished = true;
                _app.ChangeScene(new GameOverScene(_kills, _elapsed));
            }
        }

        /// <summary>冒烟用：直接把玩家打死，验证 GameOver 场景切换。</summary>
        public void DebugKillPlayer()
        {
            _player.Hp = 0f;
        }

        // ---------------- 更新 ----------------

        private void UpdatePlayer(float dt)
        {
            InputState input = _app.Input;
            float dx = 0f;
            float dy = 0f;

            if (input.Down(Keys.A) || input.Down(Keys.Left)) dx -= 1f;
            if (input.Down(Keys.D) || input.Down(Keys.Right)) dx += 1f;
            if (input.Down(Keys.W) || input.Down(Keys.Up)) dy -= 1f;
            if (input.Down(Keys.S) || input.Down(Keys.Down)) dy += 1f;

            if (dx != 0f || dy != 0f)
            {
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                dx /= len;
                dy /= len;
                _player.X += dx * Player.Speed * dt;
                _player.Y += dy * Player.Speed * dt;
                _player.FacingX = dx;
                _player.FacingY = dy;
            }

            _player.X = Clamp(_player.X, ArenaMargin + Player.Radius, _app.Width - ArenaMargin - Player.Radius);
            _player.Y = Clamp(_player.Y, ArenaMargin + Player.Radius, _app.Height - ArenaMargin - Player.Radius);

            _player.FireCooldown -= dt;
            bool wantFire = input.Down(Keys.Space) || input.Down(Keys.J) || input.MouseLeft;
            if (wantFire && _player.FireCooldown <= 0f)
            {
                _player.FireCooldown = Player.FireInterval;
                _bullets.Add(new Bullet
                {
                    X = _player.X + _player.FacingX * (Player.Radius + 4f),
                    Y = _player.Y + _player.FacingY * (Player.Radius + 4f),
                    Vx = _player.FacingX * Bullet.Speed,
                    Vy = _player.FacingY * Bullet.Speed,
                });
                _bulletsFired++;
            }
        }

        private void UpdateBullets(float dt)
        {
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                Bullet b = _bullets[i];
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;
                b.Life -= dt;
                if (b.Life <= 0f || b.X < -20f || b.Y < -20f ||
                    b.X > _app.Width + 20f || b.Y > _app.Height + 20f)
                {
                    _bullets.RemoveAt(i);
                }
            }
        }

        private void UpdateEnemies(float dt)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                Enemy e = _enemies[i];
                float dx = _player.X - e.X;
                float dy = _player.Y - e.Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.001f)
                {
                    e.X += dx / len * e.Speed * dt;
                    e.Y += dy / len * e.Speed * dt;
                }

                float touch = Player.Radius + Enemy.Radius;
                if (len < touch)
                {
                    _player.Hp -= EnemyContactDps * dt;
                }
            }
        }

        private void ResolveBulletHits()
        {
            for (int bi = _bullets.Count - 1; bi >= 0; bi--)
            {
                Bullet b = _bullets[bi];
                for (int ei = _enemies.Count - 1; ei >= 0; ei--)
                {
                    Enemy e = _enemies[ei];
                    float dx = e.X - b.X;
                    float dy = e.Y - b.Y;
                    float rr = Enemy.Radius + Bullet.Radius;
                    if (dx * dx + dy * dy > rr * rr) continue;

                    e.Hp -= b.Damage;
                    _bullets.RemoveAt(bi);
                    if (e.Hp <= 0f)
                    {
                        _enemies.RemoveAt(ei);
                        _kills++;
                    }
                    break;
                }
            }
        }

        private void UpdateSpawn(float dt)
        {
            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;

            _spawnTimer = Math.Max(0.45f, 1.3f - _elapsed * 0.02f);
            if (_enemies.Count >= MaxEnemies) return;

            float margin = ArenaMargin + 22f;
            float x, y;
            switch (_app.Rng.Next(4))
            {
                case 0:
                    x = margin;
                    y = Rand(margin, _app.Height - margin);
                    break;
                case 1:
                    x = _app.Width - margin;
                    y = Rand(margin, _app.Height - margin);
                    break;
                case 2:
                    x = Rand(margin, _app.Width - margin);
                    y = margin;
                    break;
                default:
                    x = Rand(margin, _app.Width - margin);
                    y = _app.Height - margin;
                    break;
            }
            _enemies.Add(MakeEnemy(x, y, 3f));
        }

        private Enemy MakeEnemy(float x, float y, float hp)
        {
            return new Enemy
            {
                X = x,
                Y = y,
                SpawnX = x,
                SpawnY = y,
                Hp = hp,
                Speed = 44f + (float)_app.Rng.NextDouble() * 10f,
            };
        }

        // ---------------- 渲染 ----------------

        public void Render()
        {
            float w = _app.Width;
            float h = _app.Height;

            Painter.Fill(_app, 0f, 0f, w, h, 16, 18, 26);

            // 场地网格
            for (float x = ArenaMargin; x <= w - ArenaMargin; x += 40f)
            {
                Painter.Line(_app, x, ArenaMargin, x, h - ArenaMargin, 24, 28, 40);
            }
            for (float y = ArenaMargin; y <= h - ArenaMargin; y += 40f)
            {
                Painter.Line(_app, ArenaMargin, y, w - ArenaMargin, y, 24, 28, 40);
            }
            Painter.Stroke(_app, ArenaMargin, ArenaMargin,
                           w - ArenaMargin * 2f, h - ArenaMargin * 2f, 58, 66, 92);

            // 子弹
            foreach (Bullet b in _bullets)
            {
                Painter.Fill(_app, b.X - Bullet.Radius, b.Y - Bullet.Radius,
                             Bullet.Radius * 2f, Bullet.Radius * 2f, 255, 224, 130);
            }

            // 敌人
            foreach (Enemy e in _enemies)
            {
                Painter.Sprite(_app, e.X, e.Y, 24f, 210, 96, 96);
                if (e.Hp > 1f)
                {
                    Painter.Fill(_app, e.X - 9f, e.Y - 17f, 18f * (e.Hp / 3f), 2f, 230, 120, 110, 200);
                }
            }

            // 玩家 + 朝向
            Painter.Sprite(_app, _player.X, _player.Y, 26f, 110, 210, 150);
            Painter.Line(_app, _player.X, _player.Y,
                         _player.X + _player.FacingX * 18f,
                         _player.Y + _player.FacingY * 18f, 180, 240, 210);

            RenderHud(w, h);
        }

        private void RenderHud(float w, float h)
        {
            // 生命条
            float barW = 120f;
            float hpRatio = _player.Hp <= 0f ? 0f : _player.Hp / Player.MaxHp;
            Painter.Fill(_app, 12f, 12f, barW, 10f, 40, 42, 54, 220);
            Painter.Fill(_app, 12f, 12f, barW * hpRatio, 10f, 96, 214, 140, 235);
            Painter.Stroke(_app, 12f, 12f, barW, 10f, 90, 96, 118);
            Painter.Text(_app, "HP " + (int)Math.Ceiling(Math.Max(0f, _player.Hp)), 12f, 26f);

            string secs = _elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            string stats = Texts.Pick(_app,
                "击杀 " + _kills + "  ·  波次 " + Wave + "  ·  存活 " + secs + "s",
                "KILLS " + _kills + " | WAVE " + Wave + " | TIME " + secs + "s");
            int sw = Painter.TextWidth(_app, stats);
            Painter.Text(_app, stats, w - 12f - sw, 12f);

            Painter.Text(_app, Texts.Pick(_app, "敌人 " + _enemies.Count, "ENEMIES " + _enemies.Count),
                         12f, h - 24f);
            string hint = Texts.Pick(_app, "WASD 移动 · Space 射击 · Esc 菜单",
                                          "WASD move | Space fire | Esc menu");
            int hw = Painter.TextWidth(_app, hint);
            Painter.Text(_app, hint, w - 12f - hw, h - 24f, 120, 128, 148);
        }

        // ---------------- 工具 ----------------

        private float Rand(float min, float max)
        {
            return min + (float)_app.Rng.NextDouble() * (max - min);
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}

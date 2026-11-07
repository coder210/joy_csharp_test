using System;
using System.Globalization;

namespace JoyGame
{
    /// <summary>
    /// 无窗口冒烟脚本：用模拟按键跑完
    /// Menu → Play（移动 / 射击 / 击杀）→ GameOver → 重开 Play，
    /// 并对每一环做断言。输出 "[smoke] PASS ..." / "[smoke] FAIL ..."，
    /// 失败时把进程退出码置 1（CI 据此判定）。
    /// </summary>
    internal sealed class SmokeScript
    {
        private const int AssertFrame = 150;

        private PlayScene _firstPlay;
        private bool _inPlay;
        private int _playCount;

        private float _startX;
        private float _startY;
        private float _maxMoved;
        private int _maxFired;
        private int _maxKills;
        private int _maxEnemies;
        private bool _enemyMoved;
        private bool _sawGameOver;
        private bool _killedPlayer;
        private bool _done;

        public void BeforeTick(GameApp app)
        {
            int f = app.Frame;
            InputState input = app.Input;

            // 菜单 → 游戏：第 2~3 帧按 Enter；GameOver → 重开：第 126~127 帧再按一次
            bool enter = (f >= 2 && f < 4) || (f >= 126 && f < 128);
            input.SetScripted(Keys.Return, enter);

            // 移动：10~89 帧按住右，10~59 帧按住下
            input.SetScripted(Keys.Right, f >= 10 && f < 90);
            input.SetScripted(Keys.Down, f >= 10 && f < 60);

            // 射击：20 帧起每 12 帧按 2 帧
            input.SetScripted(Keys.Space, f >= 20 && (f - 20) % 12 < 2);

            // 120 帧：直接打死玩家，验证 GameOver 场景切换
            if (f == 120 && !_killedPlayer && _firstPlay != null && app.Scene == _firstPlay)
            {
                _killedPlayer = true;
                _firstPlay.DebugKillPlayer();
            }
        }

        public void AfterTick(GameApp app)
        {
            Observe(app);
            if (_done || app.Frame < AssertFrame) return;

            _done = true;
            bool ok = true;
            ok &= ApiBindingSmoke.Passed; // IDL 新链路：强类型对象 + 自动释放
            ok &= _playCount >= 2;      // Menu→Play 与 GameOver→Play 都发生过
            ok &= _sawGameOver;
            ok &= _maxMoved > 5f;       // 输入驱动了移动
            ok &= _maxFired > 0;        // 射击生效
            ok &= _maxKills > 0;        // 子弹真的打死了敌人
            ok &= _enemyMoved;          // 敌人 AI 在追玩家

            string detail = string.Format(CultureInfo.InvariantCulture,
                "api={0} play={1} gameover={2} moved={3:0.0} fired={4} kills={5} enemies={6}",
                ApiBindingSmoke.Passed, _playCount, _sawGameOver,
                _maxMoved, _maxFired, _maxKills, _maxEnemies);

            if (ok)
            {
                Joy.Host.log("[smoke] PASS " + detail);
                Joy.Host.set_exit_code(0);
            }
            else
            {
                Joy.Host.log("[smoke] FAIL " + detail);
                Joy.Host.set_exit_code(1);
            }
            Joy.Host.quit();
        }

        private void Observe(GameApp app)
        {
            if (app.Scene is PlayScene play)
            {
                if (!_inPlay)
                {
                    _inPlay = true;
                    _playCount++;
                    if (_firstPlay == null)
                    {
                        _firstPlay = play;
                        _startX = play.PlayerX;
                        _startY = play.PlayerY;
                    }
                }

                if (play == _firstPlay)
                {
                    float dx = play.PlayerX - _startX;
                    float dy = play.PlayerY - _startY;
                    float moved = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (moved > _maxMoved) _maxMoved = moved;
                }

                if (play.BulletsFired > _maxFired) _maxFired = play.BulletsFired;
                if (play.Kills > _maxKills) _maxKills = play.Kills;
                if (play.EnemyCount > _maxEnemies) _maxEnemies = play.EnemyCount;
                if (!_enemyMoved && play.EnemyHasMoved) _enemyMoved = true;
            }
            else
            {
                _inPlay = false;
                if (app.Scene is GameOverScene) _sawGameOver = true;
            }
        }
    }
}

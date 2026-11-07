namespace JoyGame
{
    internal sealed class MenuScene : IScene
    {
        private GameApp _app;

        public void Enter(GameApp app)
        {
            _app = app;
            Joy.Host.log("[scene] Menu");
        }

        public void Update(float dt)
        {
            if (_app.Input.Pressed(Keys.Return) || _app.Input.Pressed(Keys.Space))
            {
                _app.ChangeScene(new PlayScene());
                return;
            }
            if (_app.Input.Pressed(Keys.Escape))
            {
                Joy.Host.quit();
            }
        }

        public void Render()
        {
            float w = _app.Width;
            float h = _app.Height;
            float cx = w * 0.5f;

            Painter.Fill(_app, 0f, 0f, w, h, 16, 18, 26);
            // 背景网格
            for (float x = 0f; x <= w; x += 32f)
            {
                Painter.Line(_app, x, 0f, x, h, 26, 30, 42);
            }
            for (float y = 0f; y <= h; y += 32f)
            {
                Painter.Line(_app, 0f, y, w, y, 26, 30, 42);
            }

            Painter.Sprite(_app, cx, h * 0.5f - 74f, 34f, 90, 200, 140);
            Painter.TextCenter(_app, "JOY C# TOPDOWN", cx, h * 0.5f - 26f, big: true, r: 120, g: 220, b: 255);
            Painter.TextCenter(_app,
                Texts.Pick(_app, "C# 业务逻辑 + joy 引擎（Mono 宿主）", "C# gameplay + joy engine (CoreCLR)"),
                cx, h * 0.5f + 4f, r: 150, g: 160, b: 180);

            if ((_app.Frame / 30) % 2 == 0)
            {
                Painter.TextCenter(_app, Texts.Pick(_app, "Enter / Space 开始", "Enter / Space to start"),
                                   cx, h * 0.5f + 44f, r: 255, g: 226, b: 140);
            }
            Painter.TextCenter(_app,
                Texts.Pick(_app, "WASD 移动 · Space/鼠标左键 射击 · Esc 退出",
                                "WASD move | Space/LMB fire | Esc quit"),
                cx, h * 0.5f + 76f, r: 130, g: 138, b: 158);
            Painter.TextCenter(_app, Texts.Pick(_app, "场景：Menu", "Scene: Menu"),
                               cx, 18f, r: 90, g: 96, b: 112);
        }
    }
}

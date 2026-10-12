using System;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.IO;
using ClassicUO.Utility;

namespace ClassicUO.Network.PacketHandlers;

internal static class EnterWorld
{
    public static void Receive(World world, ref StackDataReader p)
    {
        uint serial = p.ReadUInt32BE();

        world.CreatePlayer(serial);

        p.Skip(4);
        world.Player.Graphic = p.ReadUInt16BE();
        world.Player.CheckGraphicChange();
        ushort x = p.ReadUInt16BE();
        ushort y = p.ReadUInt16BE();
        sbyte z = (sbyte)p.ReadUInt16BE();

        if (world.Map == null)
            world.MapIndex = 0;

        world.Player.SetInWorldTile(x, y, z);
        world.Player.Direction = (Direction)(p.ReadUInt8() & 0x7);
        world.RangeSize.X = x;
        world.RangeSize.Y = y;

        if (
            ProfileManager.CurrentProfile != null
            && ProfileManager.CurrentProfile.UseCustomLightLevel
        )
            world.Light.Overall =
                ProfileManager.CurrentProfile.LightLevelType == 1
                    ? Math.Min(world.Light.Overall, ProfileManager.CurrentProfile.LightLevel)
                    : ProfileManager.CurrentProfile.LightLevel;

        Client.Game.Audio.UpdateCurrentMusicVolume();

        // DM Test 2: reproduce the official client's opening packet order.
        // Opt-in, version-limited and reversible; the regular path stays intact.
        bool dmHandshakeTest =
            Settings.GlobalSettings.ClientVersion == "6.0.14.3"
            && Environment.GetEnvironmentVariable("TAZUO_DM_HANDSHAKE_TEST") == "1";

        if (dmHandshakeTest)
        {
            AsyncNetClient.Socket.Send_ClientVersion(Settings.GlobalSettings.ClientVersion);

            // Original DM client sends non-zero viewport parameters here.
            // During EnterWorld, TazUO may still have a zero-sized camera.
            uint width = (uint)Client.Game.Scene.Camera.Bounds.Width;
            uint height = (uint)Client.Game.Scene.Camera.Bounds.Height;
            if (width == 0 || height == 0)
            {
                width = 800;
                height = 600;
            }

            // DM Test 3: compare BF/05's second uint with the original client capture.
            // This is a single-field experiment; no effect unless both flags are enabled.
            // Use the raw hex value via environment variable, never a baked-in constant.
            if (Environment.GetEnvironmentVariable("TAZUO_DM_VIEWPORT_TEST") == "1")
            {
                string rawHeight = Environment.GetEnvironmentVariable("TAZUO_DM_VIEWPORT_RAW_HEIGHT");

                if (
                    !string.IsNullOrWhiteSpace(rawHeight)
                    && uint.TryParse(
                        rawHeight,
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out uint parsedHeight
                    )
                )
                {
                    height = parsedHeight;
                }
            }

            AsyncNetClient.Socket.Send_GameWindowSize(width, height);
            AsyncNetClient.Socket.Send_Language(Settings.GlobalSettings.Language);
            // Send after version, size, language (same sequence as official DM client).
            AsyncNetClient.Socket.Send_ClientType();
        }
        else
        {
            if (Client.Game.UO.Version >= ClassicUO.Utility.ClientVersion.CV_200)
            {
                if (ProfileManager.CurrentProfile != null)
                    AsyncNetClient.Socket.Send_GameWindowSize(
                        (uint)Client.Game.Scene.Camera.Bounds.Width,
                        (uint)Client.Game.Scene.Camera.Bounds.Height
                    );

                AsyncNetClient.Socket.Send_Language(Settings.GlobalSettings.Language);
            }

            AsyncNetClient.Socket.Send_ClientVersion(Settings.GlobalSettings.ClientVersion);
        }

        GameActions.SingleClick(world, world.Player);
        AsyncNetClient.Socket.Send_SkillsRequest(world.Player.Serial);

        if (world.Player.IsDead)
            world.ChangeSeason(ClassicUO.Game.Managers.Season.Desolation, 42);

        if (
            Client.Game.UO.Version >= ClassicUO.Utility.ClientVersion.CV_70796
            && ProfileManager.CurrentProfile != null
        )
            AsyncNetClient.Socket.Send_ShowPublicHouseContent(
                ProfileManager.CurrentProfile.ShowHouseContent
            );

        AsyncNetClient.Socket.Send_ToPlugins_AllSkills();
        AsyncNetClient.Socket.Send_ToPlugins_AllSpells();

        if (ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.WebMapAutoStart &&
            !MapWebServerManager.Instance.IsRunning)
            _ = MapWebServerManager.Instance.Start();
    }
}

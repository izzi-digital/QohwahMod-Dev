﻿using GTA;
using GTA.Math;
using GTA.Native;
using LemonUI;
using LemonUI.Menus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public class Qohwah : Script
{
    private int wins = 0;
    private readonly string winsPath = "scripts/QohwahWins.ini";
    private readonly string racesPath = "scripts/QohwahRaces.ini";
    private readonly string configPath = "scripts/QohwahConfig.ini";
    private ObjectPool menuPool = new ObjectPool();
    private NativeMenu menu;
    private List<(string Name, Vector3 Start, float StartYaw, Vector3 Finish, float FinishYaw)> races = new List<(string, Vector3, float, Vector3, float)>();
    private int currentRaceIndex = -1;
    private bool isRacing = false;
    private float radius = 5f;
    private float countdown = 0f;
    private bool isCountingDown = false;
    private Blip raceBlip = null;
    private bool hasDied = false;
    private int respawnCooldownFrames = 0;
    private Vehicle raceVehicle = null;
    private readonly Model raceVehicleModel = new Model(VehicleHash.Sultan); 
    private Keys customKey = Keys.F5;
    private bool autoDecreaseWins = true;
    private Keys manualDecreaseKey = Keys.Subtract;

    public Qohwah()
    {
        Tick += OnTick;
        KeyDown += OnKeyDown;

        LoadConfig();
        LoadWins();
        LoadRaces();
        SetupMenu();
        GTA.UI.Screen.ShowSubtitle("Qohwah Mod Loaded!");
    }
    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(configPath)) return;
            var lines = File.ReadAllLines(configPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("TriggerKey="))
                {
                    string keyName = line.Substring("TriggerKey=".Length).Trim();
                    if (Enum.TryParse(keyName, out Keys parsedKey))
                    {
                        customKey = parsedKey;
                    }
                }
                else if (line.StartsWith("AutoDecreaseWinsOnDeath", StringComparison.OrdinalIgnoreCase))
                {
                    autoDecreaseWins = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                else if (line.StartsWith("ManualDecreaseKey", StringComparison.OrdinalIgnoreCase))
                {
                    var keyPart = line.Split('=')[1].Trim();
                    Enum.TryParse(keyPart, out manualDecreaseKey);
                }
            }
        }
        catch { }
    }

    private void SetupMenu()
    {
        menu = new NativeMenu("Qohwah", "Race System");
        menuPool.Add(menu);

        for (int i = 0; i < races.Count; i++)
        {
            var item = new NativeItem($"{races[i].Name}");
            int index = i;
            item.Activated += (sender, e) =>
            {
                currentRaceIndex = index;
                StartRace(index);
                menu.Visible = false;
            };
            menu.Add(item);
        }
    }

    private void StartRace(int index)
    {
        Vector3 start = races[index].Start;
        float heading = races[index].StartYaw;

        Game.Player.Character.Position = start;
        Game.Player.Character.Heading = heading; 
        Game.Player.Character.MaxHealth = 200;
        Game.Player.Character.Health = 200;
        Game.Player.Character.Armor = 100;
        Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, Game.Player.Character, false);

        Game.Player.Character.Task.ClearAllImmediately();

        SpawnRaceVehicle(start, heading);

        isRacing = true;
        isCountingDown = false;
        hasDied = false;

        if (raceBlip != null) raceBlip.Delete();
        raceBlip = World.CreateBlip(races[index].Finish);
        raceBlip.Color = BlipColor.Blue;
        raceBlip.Name = "Race Finish";
        raceBlip.ShowRoute = true;

        GTA.UI.Screen.ShowSubtitle("Start Racing!", 3000);
        hasDied = false;
    }

    private void SpawnRaceVehicle(Vector3 position, float heading)
    {
        if (raceVehicle != null)
        {
            raceVehicle.Delete();
            raceVehicle = null;
        }

        raceVehicleModel.Request(1000);
        if (raceVehicleModel.IsInCdImage && raceVehicleModel.IsValid)
        {
            raceVehicle = World.CreateVehicle(raceVehicleModel, position, heading);
            Game.Player.Character.SetIntoVehicle(raceVehicle, VehicleSeat.Driver);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == customKey)
        {
            menu.Visible = !menu.Visible;
        }

        if (e.KeyCode == manualDecreaseKey)
        {
            wins--;
            SaveWins();
            GTA.UI.Screen.ShowSubtitle($"Wins manually decreased: {wins}", 3000);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        menuPool.Process();

        float health = Game.Player.Character.Health;
        float maxHealth = Game.Player.Character.MaxHealth;
        float healthPercent = Math.Max(0, Math.Min(health / maxHealth, 1f));

        float barWidth = 0.1f;
        float barHeight = 0.03f;
        float barX = 0.5f;
        float barY = 0.94f;

        Function.Call(Hash.DRAW_RECT, barX, barY, barWidth, barHeight, 50, 50, 50, 200);
        Function.Call(Hash.DRAW_RECT,
            barX - (barWidth / 2) * (1 - healthPercent),
            barY,
            barWidth * healthPercent,
            barHeight,
            0, 200, 0, 255);

        float textScale = Math.Min(1.0f, barHeight * 15f);
        float textY = barY - (textScale * 0.03f);
        string healthText = $"{(healthPercent * 100f):0}%";

        Function.Call(Hash.SET_TEXT_FONT, 7);
        Function.Call(Hash.SET_TEXT_SCALE, textScale, textScale);
        Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 255);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.SET_TEXT_OUTLINE);
        Function.Call(Hash.SET_TEXT_WRAP, barX - barWidth / 2, barX + barWidth / 2);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, healthText);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, barX, textY);

        if (!isRacing) return;

        var race = races[currentRaceIndex];

        float dist = Game.Player.Character.Position.DistanceTo(race.Finish);
        float totalDist = race.Start.DistanceTo(race.Finish);
        float done = Game.Player.Character.Position.DistanceTo(race.Start);
        float progress = Math.Min(100f, (done / totalDist) * 100f);

        DrawText($"~o~Wins: {wins}", 0.5f, 0.05f, 1.0f, 255, 165, 0);
        DrawText($"~g~{Math.Round(progress)}%", 0.5f, 0.11f, 1.0f, 0, 255, 100);

        // draw finish marker
        World.DrawMarker(
            MarkerType.Cylinder,
            race.Finish,
            Vector3.Zero, // Direction
            Vector3.Zero, // Rotation
            new Vector3(2f, 2f, 2f), // Scale
            System.Drawing.Color.Blue
        );

        if (dist < radius && !isCountingDown)
        {
            countdown = 10f;
            isCountingDown = true;

            if (raceBlip != null)
            {
                raceBlip.Delete();
                raceBlip = null;
            }
        }

        if (isCountingDown)
        {
            // Batalkan countdown jika keluar radius finish
            if (Game.Player.Character.Position.DistanceTo(race.Finish) > radius)
            {
                isCountingDown = false;
                GTA.UI.Screen.ShowSubtitle("You moved! Countdown canceled.", 3000);
            }
            else
            {
                countdown -= Game.LastFrameTime;
                DrawText($"~r~{Math.Ceiling(countdown)}", 0.5f, 0.17f, 1.2f, 255, 0, 0);

                if (countdown <= 0f)
                {
                    wins++;
                    SaveWins();
                    StartRace(currentRaceIndex);
                    GTA.UI.Screen.ShowSubtitle("Wins: " + wins, 3000);
                    //GTA.UI.Screen.ShowSubtitle("Restarted!", 3000);
                }
            }
        }

        if (Game.Player.Character.IsDead && !hasDied)
        {
            hasDied = true;

            Function.Call(Hash.RESURRECT_PED, Game.Player.Character.Handle);
            Game.Player.Character.MaxHealth = 200;
            Game.Player.Character.Health = 200;
            Game.Player.Character.Armor = 100; 

            if (autoDecreaseWins)
            {
                wins--;
                SaveWins();
            }

            respawnCooldownFrames = 900; // delay 15 detik (900 frame)
        }

        if (respawnCooldownFrames > 0)
        {
            respawnCooldownFrames--;
            if (respawnCooldownFrames == 0)
            {
                StartRace(currentRaceIndex);
                GTA.UI.Screen.ShowSubtitle("You died! Restarting race...", 3000);
            }
        }
    }

    private void DrawText(string text, float x, float y, float scale, int r, int g, int b)
    {
        Function.Call(Hash.SET_TEXT_FONT, 7);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.SET_TEXT_OUTLINE);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }

    private void LoadWins()
    {
        try
        {
            if (File.Exists(winsPath))
            {
                string text = File.ReadAllText(winsPath);
                int.TryParse(text, out wins);
            }
        }
        catch { }
    }

    private void SaveWins()
    {
        try
        {
            File.WriteAllText(winsPath, wins.ToString());
        }
        catch { }
    }

    private void LoadRaces()
    {
        try
        {
            if (!File.Exists(racesPath)) return;
            var lines = File.ReadAllLines(racesPath);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length == 9)
                {
                    string name = parts[0];
                    float.TryParse(parts[1], out float sx);
                    float.TryParse(parts[2], out float sy);
                    float.TryParse(parts[3], out float sz);
                    float.TryParse(parts[4], out float syaw);
                    float.TryParse(parts[5], out float fx);
                    float.TryParse(parts[6], out float fy);
                    float.TryParse(parts[7], out float fz);
                    float.TryParse(parts[8], out float fyaw);
                    races.Add((name, new Vector3(sx, sy, sz), syaw, new Vector3(fx, fy, fz), fyaw));
                }
            }
        }
        catch { }
    }
}

﻿using System;
using System.IO;
using System.Reflection;
using Harmony;
using UnityEngine;

/* Todo:
 * - game crashes on exit, see exception below
 * - select PB run, or let user select run
 * 
 * - It'd be nice to see the ghost using shortcuts, with correct color, _currentGhostRoom.BlinkShortCut();
 * - Could we still use abstractphysicalobject, and set its collision flags and such to false?
 * - Show the split times
 * - Implement the wait-for-player-to-catch-up mechanic, showing the splits
 * - Record and render more slugcat state
 * 
 * 
ObjectDisposedException: The object was used after being disposed.
System.IO.StreamWriter.Write (string) <IL 0x00015, 0x0005c>
System.IO.TextWriter.WriteLine (string) <IL 0x00002, 0x0002d>
ReplayGhostMod.ReplayGhostMod.UpdateRecording () <IL 0x00094, 0x003de>
ReplayGhostMod.ReplayGhostMod.RainWorldGame_Update_Post (RainWorldGame) <IL 0x00022, 0x000d7>
(wrapper dynamic-method) RainWorldGame.Update_Patch1 (object) <IL 0x0058d, 0x01354>
MainLoopProcess.RawUpdate (single) <IL 0x00027, 0x00081>
RainWorldGame.RawUpdate (single) <IL 0x00611, 0x01757>
ProcessManager.Update (single) <IL 0x00037, 0x000cc>
RainWorld.Update () <IL 0x0000b, 0x0003f>
 */



namespace ReplayGhostMod {
    /// <summary>
    /// Replay Ghost Mod, by mzandvliet
    /// </summary>
    public static class ReplayGhostMod {
        private const string RecordingFolder =
            "D:\\Games\\SteamLibrary\\steamapps\\common\\Rain World\\Mods\\ReplayGhostMod\\Replays";

        private static AbstractCreature _player;
        private static ReplayGhost _ghost;
        private static ReplayGhostGraphics _ghostGraphics;

        private static TextWriter _writer;
        private static StreamReader _replay;

        private static WorldCoordinate _ghostWorldCoords;
        private static WorldCoordinate _playerWorldCoords;

        public static void Initialize() {
            PatchHooks();
        }

        private static void PatchHooks() {
            var harmony = HarmonyInstance.Create("com.mzandvliet.rainworld.mod.replayghostmod");

            var ctor = typeof(RainWorldGame).GetConstructor(new Type[] { typeof(ProcessManager) });
            var hook = typeof(ReplayGhostMod).GetMethod("RainWorldGame_Ctor_Post");
            harmony.Patch(ctor, null, new HarmonyMethod(hook));

            var update = typeof(RainWorldGame).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public);
            hook = typeof(ReplayGhostMod).GetMethod("RainWorldGame_Update_Post");
            harmony.Patch(update, null, new HarmonyMethod(hook));

            var exitGame = typeof(RainWorldGame).GetMethod("ExitGame", BindingFlags.Instance | BindingFlags.NonPublic);
            hook = typeof(ReplayGhostMod).GetMethod("RainWorldGame_ExitGame_Pre");
            harmony.Patch(exitGame, new HarmonyMethod(hook), null);
        }

        public static void RainWorldGame_Ctor_Post(RainWorldGame __instance, ProcessManager manager) {
            _player = __instance.session.Players[0];

            if (!Directory.Exists(RecordingFolder)) {
                Directory.CreateDirectory(RecordingFolder);
            }
            
            LoadReplay();
            StartRecording();

            _ghost = new ReplayGhost();
        }

        private static void StartRecording() {
            _writer = new StreamWriter(Path.Combine(RecordingFolder, GetNewReplayFileName()), false);
        }

        private static void LoadReplay() {
            var replays = Directory.GetFiles(RecordingFolder);
            if (replays.Length == 0) {
                Debug.Log("No recorded runs available");
                return;
            }

            var replay = replays[0];
            _replay = File.OpenText(replay);
        }

        //private void ExitGame(bool asDeath, bool asQuit)
        public static void RainWorldGame_ExitGame_Pre(RainWorldGame __instance, bool asDeath, bool asQuit) {
            _writer.Close();
            // Todo: last line goes wrong sometimes
        }

        public static void RainWorldGame_Update_Post(RainWorldGame __instance) {
            if (_player?.realizedCreature == null) {
                return;
            }

            UpdatePlayback();
            UpdateRecording();
        }

        private static void UpdatePlayback() {
            if (_replay != null && !_replay.EndOfStream) { 
                string line = _replay.ReadLine();

                WorldCoordinate ghostWorldCoords;
                Vector2 ghostPos;
                Vector2 ghostRot;
                Read(line, out ghostWorldCoords, out ghostPos, out ghostRot);

                _ghost.Pos = ghostPos;
                _ghost.Rot = ghostRot;

                if (ghostWorldCoords.room != _ghostWorldCoords.room) {
                    MoveGhostSpriteToRoom(ghostWorldCoords);
                }

                if (_player.pos.room != _playerWorldCoords.room) {
                    MoveGhostSpriteToRoom(ghostWorldCoords);
                }
            }
        }

        private static void UpdateRecording() {
            var worldCoord = _player.realizedCreature.coord.SaveToString();
            var chunkPos = _player.realizedCreature.mainBodyChunk.pos;
            var chunkRot = _player.realizedCreature.mainBodyChunk.Rotation;
            _writer.WriteLine($"{worldCoord}|{chunkPos.x},{chunkPos.y}|{chunkRot.x},{chunkRot.y}");
        }

        private static void MoveGhostSpriteToRoom(WorldCoordinate ghostWorldCoordinate) {
            if (_ghostGraphics != null) {
                _ghostGraphics.Destroy();
                _player.world.GetAbstractRoom(_ghostWorldCoords.room).realizedRoom.RemoveObject(_ghostGraphics);
            }

            Room activeRoom = _player.world.GetAbstractRoom(ghostWorldCoordinate.room).realizedRoom;
            if (activeRoom != null) {
                _ghostGraphics = new ReplayGhostGraphics(_ghost);
                activeRoom.AddObject(_ghostGraphics);
            }

            _ghostWorldCoords = ghostWorldCoordinate;
            _playerWorldCoords = _player.pos;
        }

        private static void Read(string line, out WorldCoordinate c, out Vector2 p, out Vector2 r) {
            var parts = line.Split(new[] {"|"}, StringSplitOptions.RemoveEmptyEntries);
            c = WorldCoordinate.FromString(parts[0]);
            p = ReadVector2(parts[1]);
            r = ReadVector2(parts[2]);
        }

        private static Vector2 ReadVector2(string line) {
            var parts = line.Split(new [] {","}, StringSplitOptions.RemoveEmptyEntries);
            var v = new Vector2(
                float.Parse(parts[0]),
                float.Parse(parts[1]));
            return v;
        }


        private static string GetNewReplayFileName() {
            DateTime now = DateTime.Now;
            return $"Replay_{now.Day}-{now.Month}-{now.Year}-{now.Hour}-{now.Minute}-{now.Ticks}.txt";
        }
    }
}

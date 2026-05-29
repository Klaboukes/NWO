using System;
using System.Collections.Generic;
using Godot;

namespace NWO.Audio;

// The project's single autoload (registered in project.godot at /root/AudioManager).
// A Node so its AudioStreamPlayer pool persists across ChangeSceneToFile, giving
// MainMenu, WorldMap, and VictoryScreen one shared sound channel.
//
// Placeholder policy: each Sfx resolves once at startup to a real
// res://assets/audio/<name>.ogg when that file exists, otherwise to a tone
// synthesized in code — so audio works with no committed binaries and real clips
// can be dropped in later with no code change (see docs/ROADMAP.md P6.3).
//
// Callers use AudioManager.Instance?.Play(...): Instance is null under xUnit (no
// autoload runs there), so the null-conditional makes every trigger a safe no-op.
public partial class AudioManager : Node
{
    public static AudioManager? Instance { get; private set; }

    private const int   PoolSize = 8;     // simultaneous voices (round-robin)
    private const int   MixRate  = 22050; // plenty for placeholder tones

    private readonly List<AudioStreamPlayer> _players = new();
    private readonly Dictionary<Sfx, AudioStream> _clips = new();
    private int _next;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _players.Add(player);
        }

        // (frequency Hz, duration ms) chosen so each effect is recognizably distinct.
        _clips[Sfx.Click]     = ResolveClip(Sfx.Click,     880, 60);
        _clips[Sfx.Move]      = ResolveClip(Sfx.Move,      440, 90);
        _clips[Sfx.Attack]    = ResolveClip(Sfx.Attack,    160, 180);
        _clips[Sfx.CityFound] = ResolveClip(Sfx.CityFound, 660, 260);
        _clips[Sfx.Win]       = ResolveClip(Sfx.Win,       990, 500);
        _clips[Sfx.Lose]      = ResolveClip(Sfx.Lose,      120, 600);
    }

    public void Play(Sfx sfx)
    {
        if (!_clips.TryGetValue(sfx, out var stream) || stream == null) return;
        var player = _players[_next];
        _next = (_next + 1) % _players.Count;
        player.Stream = stream;
        player.Play();
    }

    // Prefer a real clip if the artist has dropped one in; otherwise synthesize.
    private static AudioStream ResolveClip(Sfx sfx, float freqHz, int ms)
    {
        string path = $"res://assets/audio/{sfx.ToString().ToLowerInvariant()}.ogg";
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<AudioStream>(path) is { } clip)
            return clip;
        return MakeTone(freqHz, ms);
    }

    // Build a 16-bit mono sine tone with a short linear fade-out so it doesn't click.
    private static AudioStreamWav MakeTone(float freqHz, int ms)
    {
        int samples = MixRate * ms / 1000;
        var data    = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t        = i / (float)MixRate;
            float envelope = 1f - i / (float)samples;              // linear fade to zero
            float value    = MathF.Sin(t * freqHz * MathF.Tau) * envelope * 0.4f;
            short pcm       = (short)(Mathf.Clamp(value, -1f, 1f) * short.MaxValue);
            data[i * 2]     = (byte)(pcm & 0xFF);
            data[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }
}

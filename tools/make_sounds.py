#!/usr/bin/env python3
"""Synthesises AVATacScan's sound effects and jingles into ../src/AVATacScan/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, saw, sine and noise voices, in the spirit
of the hiss-and-whoosh discrete sound boards of the early-eighties vector machines. The jingles are new
compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVATacScan", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def gain(part, g):
    return [v * g for v in part]


def lowpass(samples, cutoff):
    a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    out, y = [], 0.0
    for v in samples:
        y += a * (v - y)
        out.append(y)
    return out


def echoed(dry, taps=((0.09, 0.3), (0.19, 0.15))):
    """The ring of a big arcade cabinet's loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.4)
    for delay, g in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def seq(bpm, notes, kind="pulse", g=0.55, release=0.6, vibrato=0.0, legato=1.0):
    """A line of (note, beats); '-' is a rest. Notes can be cut short (legato below 1) for a detached feel."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
            continue
        sounding = tone(d * legato, hz(note), kind=kind, release=release, gain=g, vibrato=vibrato, vib_rate=6)
        out.extend(sounding + silence(d - len(sounding) / RATE))
    return out


def fit(part, seconds):
    n = int(RATE * seconds)
    return (part + [0.0] * n)[:n]


def drums(bpm, beats, pattern):
    """A drum track: pattern is a string per beat - k kick, s snare, h hat, '.' nothing."""
    beat = 60.0 / bpm
    out = []
    for i in range(beats):
        c = pattern[i % len(pattern)]
        if c == "k":
            hit = tone(beat, 130, 45, "sine", attack=0.002, release=2.2, gain=0.9)
        elif c == "s":
            hit = mix(tone(beat, 2200, 900, "noise", attack=0.001, release=3.0, gain=0.45), tone(beat * 0.5, 220, 160, "tri", release=2, gain=0.3))
        elif c == "h":
            hit = tone(beat * 0.5, 7000, 6500, "noise", attack=0.001, release=4, gain=0.16)
        else:
            hit = []
        out.extend(fit(hit, beat))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The squadron's volley: several lasers at once, a little apart.
    shot = tone(0.16, 2400, 380, "pulse", attack=0.001, release=1.3, gain=0.6)
    write("Fire.wav", mix(shot, silence(0.012) + tone(0.15, 2100, 330, "pulse", attack=0.001, release=1.3, gain=0.4),
                          tone(0.05, 6000, 3000, "noise", attack=0.001, release=2, gain=0.35)), 0.3)
    write("Blast.wav", mix(lowpass(tone(0.55, 3500, 300, "noise", attack=0.001, release=1.3), 2600), tone(0.3, 180, 50, "sine", attack=0.002, release=1.2, gain=0.7)), 0.5)
    # A ship of the squadron gone: a long roaring crash with a falling whine in it.
    write("ShipLost.wav", cat(mix(lowpass(tone(1.3, 2600, 100, "noise", attack=0.002, release=0.8), 1500),
                                  tone(1.1, 1400, 90, "saw", vibrato=0.05, vib_rate=14, release=0.9, gain=0.45),
                                  tone(1.0, 80, 30, "sine", release=0.8, gain=0.8)),
                              lowpass(tone(0.5, 800, 200, "noise", attack=0.02, release=1.2, gain=0.3), 800)), 0.62)
    write("EnemyFire.wav", tone(0.22, 1300, 260, "saw", attack=0.003, release=0.9, vibrato=0.1, vib_rate=40), 0.22)
    write("Bomb.wav", tone(0.45, 1800, 400, "sine", attack=0.005, release=0.6, gain=0.8), 0.25)
    write("Clang.wav", mix(tone(0.12, 1250, 1150, "square", attack=0.001, release=2.2, gain=0.5), tone(0.1, 3100, 2900, "sine", attack=0.001, release=2.4, gain=0.4)), 0.25)
    # The warp into the tunnel: a rising rush of noise and a climbing tone, swelling then cut.
    rush = lowpass(tone(2.2, 300, 6000, "noise", attack=1.2, release=0.4), 3000)
    climb = tone(2.2, 120, 1800, "saw", attack=1.0, release=0.4, vibrato=0.02, vib_rate=9, gain=0.5)
    write("Warp.wav", mix(rush, climb, tone(2.2, 60, 240, "square", attack=1.4, release=0.3, gain=0.25)), 0.5)
    # The mother ship coming: a deep pulsing hum with a beacon over it.
    hum = tone(3.0, 55, 58, "saw", attack=0.4, release=0.4, vibrato=0.04, vib_rate=3, gain=0.8)
    beacon = cat(*[tone(0.25, 1320, kind="sine", release=1.5, gain=0.5) + silence(0.35) for _ in range(5)])
    write("MotherShip.wav", mix(lowpass(hum, 500), beacon), 0.5)
    write("Docked.wav", mix(seq(180, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("E6", 0.5), ("G6", 0.5), ("C7", 3)], kind="pulse", g=0.5, release=0.4),
                            seq(180, [("C3", 2), ("G2", 1), ("C3", 3)], kind="tri", g=0.6)), 0.4)
    write("DockFailed.wav", seq(150, [("G4", 0.5), ("F#4", 0.5), ("F4", 0.5), ("E4", 2)], kind="square", g=0.4, release=0.6), 0.35)
    write("ExtraShip.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["E6", "G#6", "B6", "E7", "G#7", "B7"]], tone(0.3, hz("E7"), kind="pulse", release=0.6)), 0.3)
    write("WaveClear.wav", seq(220, [("G5", 0.5), ("B5", 0.5), ("D6", 0.5), ("G6", 1.5)], kind="pulse", g=0.5, release=0.5), 0.32)
    # The round's start: an original fanfare in D, horns over a pulsing bass.
    write("RoundTune.wav", mix(seq(150, [("A4", 0.5), ("D5", 0.5), ("F#5", 0.5), ("A5", 1), ("F#5", 0.5), ("A5", 0.5), ("D6", 2.5)], kind="saw", g=0.45, release=0.4, vibrato=0.006),
                               seq(150, [(n, 0.5) for n in ["D3", "D3", "A2", "D3", "D3", "A2", "D3", "A2", "D3", "D3", "D2", "D2"]], kind="tri", g=0.6, legato=0.8),
                               drums(300, 12, "k.h.s.h.k.k.")), 0.4)
    write("GameOver.wav", mix(seq(70, [("A4", 1), ("F4", 1), ("D4", 1), ("C#4", 1), ("D4", 4)], kind="tri", g=0.5),
                              seq(70, [("D3", 2), ("A#2", 2), ("A2", 2), ("D2", 2)], kind="tri", g=0.6)), 0.42)


if __name__ == "__main__":
    main()

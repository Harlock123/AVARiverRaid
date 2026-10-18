#!/usr/bin/env python3
"""Synthesises AVARiverRaid's sound effects and tunes into ../src/AVARiverRaid/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine and noise voices. The marches
are new compositions. The low-fuel call is spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVARiverRaid", "Sounds")
RNG = random.Random(1984)


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


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Fire.wav", tone(0.12, 1600, 600, "square", attack=0.001, release=0.6, gain=0.5), 0.12)
    write("Boom.wav", mix(tone(0.35, 160, 50, "noise", attack=0.001, release=1.0), tone(0.25, 90, 40, "sine", release=1.0)), 0.36)
    write("Bridge.wav", mix(tone(0.9, 120, 30, "noise", attack=0.001, release=1.2), tone(0.7, 70, 30, "sine", release=1.0)), 0.48)
    write("Crash.wav", mix(tone(1.0, 200, 30, "noise", attack=0.001, release=1.0), tone(0.8, 60, 25, "sine", release=1.0)), 0.5)
    write("Refuel.wav", cat(tone(0.08, 880, kind="square", release=0.3, gain=0.4), tone(0.08, 1320, kind="square", release=0.3, gain=0.4)), 0.12)
    write("LowFuel.wav", cat(*[tone(0.15, f, kind="square", release=0.2) for f in [440, 330, 440, 330]]), 0.22)
    write("Extra.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7"]]), 0.24)
    words = "F Y UW1 L , L OW1 !"
    write("SayFuel.wav", mix(say(words, rate=RATE, pitch=105, speed=1.0, size=1.05), [s * 0.3 for s in say(words, rate=RATE, pitch=158, speed=1.0, size=1.05, seed=11)]), 0.5)
    # In flight: the engine's drone with a snare and a march of its own over it.
    e = 0.5
    lead = [("G4", e), ("C5", e), ("E5", e), ("G5", 1.5), ("E5", e), ("G5", 1), ("C6", 2), ("B5", e), ("A5", e), ("G5", e), ("F5", e), ("E5", 1), ("D5", 1),
            ("F4", e), ("B4", e), ("D5", e), ("F5", 1.5), ("D5", e), ("F5", 1), ("B5", 2), ("A5", e), ("G5", e), ("F5", e), ("D5", e), ("C5", 2)]
    bass = [("C3", 1), ("G2", 1)] * 4 + [("G2", 1), ("D3", 1)] * 4 + [("G2", 1), ("G2", 1), ("C3", 2)]
    snare = []
    for k in range(int(sum(b for _, b in lead) * 2)):
        snare += tone(0.06, 2000, 900, "noise", attack=0.001, release=1.0, gain=0.3 if k % 2 else 0.12) + silence(60 / 132 / 2 - 0.06)
    m = tune(132, lead, bass, kind="pulse")
    write("Music.wav", mix(m, snare[:len(m)]), 0.26)
    title = [("C5", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("C5", 1), ("G5", 2), ("E5", 1), ("C5", 1), ("D5", 1), ("D5", 0.5), ("E5", 0.5), ("F5", 1), ("D5", 1), ("G5", 3), ("-", 1)]
    tb = [("C3", 1), ("G2", 1)] * 4 + [("G2", 1), ("D3", 1)] * 3 + [("G2", 1), ("-", 1)]
    write("TitleTune.wav", cat(tune(120, title, tb, kind="pulse"), silence(1.0)), 0.32)
    write("Victory.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 3)],
                              [("C3", 1), ("G2", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("C3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], [("A2", 2), ("F2", 2), ("A1", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()

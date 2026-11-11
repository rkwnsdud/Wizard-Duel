# Arcane Bolt Fire: source and license

- Source: **Magic Smite** by **spookymodem (Freesound)**, shared by
  **freesound_community** on [Pixabay](https://pixabay.com/sound-effects/film-special-effects-magic-smite-6012/).
- Source file supplied by the project user: `freesound_community-magic-smite-6012.mp3`.
  SHA-256: `1BDC4A0068953F57D9E774A48D16D7523F5330F97B447076E22F507B2004CFDA`.
  The source file was read but not modified.
- License: [Pixabay Content License](https://pixabay.com/service/terms/),
  as indicated on the sound's page. It permits modification and use in a
  creative work without mandatory attribution. It restricts standalone
  redistribution of the audio, including a version that is only cropped or
  converted. This edited asset is **not** covered by the project's 0BSD
  procedural-sound license.
- Edited file: `Assets/Audio/BasicWand/ArcaneBolt_Fire.wav`.
  SHA-256: `2E5AD1397BF9DF1C51AAD0B23E105CAF997D5A254BA6770013FD804FAA2395A7`.

## Edit record

Windows Media Foundation decoded the 3.074-second MP3 to 24 kHz stereo PCM.
Analysis of 10 ms RMS windows placed the prominent launch activity at about
0.93–1.25 seconds, with a quieter tail after it. The edit uses source time
**0.900–1.400 seconds**: a short lead into the launch and some original tail.

The selected 0.5 seconds were resampled to 48 kHz with a 32-tap Lanczos
interpolator, preserving playback speed and pitch. An 8 ms fade-in removes a
start click; a 120 ms raised-cosine fade-out makes the shortened tail resolve
smoothly. A uniform 1.6× gain (~4.1 dB) was applied without compression or
limiting. No generated layers, low-frequency boost, or other sounds were added.
The output is stereo 16-bit PCM WAV with a peak of about 0.592 (-4.6 dBFS).

The edit is prepared for Unity import. On the user's follow-up request, the
`BasicWand.prefab` fireSound reference was updated to this clip. No scene or
gameplay script was changed.

The Basic Wand's dedicated Unity AudioSource was later set to pitch `1.1` at
the user's request. This changes the in-game playback only; the edited WAV
and its source-preserving edit record above remain unchanged.

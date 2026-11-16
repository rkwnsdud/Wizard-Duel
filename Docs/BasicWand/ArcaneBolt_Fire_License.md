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
- Edited file: `Assets/Audio/Clips/BasicWand/ArcaneBolt_Fire.wav`.
  SHA-256 right after the edit below: `2E5AD1397BF9DF1C51AAD0B23E105CAF997D5A254BA6770013FD804FAA2395A7`.
  When the sound effects were gathered into one library, the file's level was
  matched to the other effects (gain only, -14 LUFS over the loudest 400 ms
  window). Current SHA-256: `640C180A8C001A29068CEF8275488D099EF9488046EA0054973DA5425971994F`.

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
The output was stereo 16-bit PCM WAV with a peak of about 0.592 (-4.6 dBFS)
before the later level match.

The edit is prepared for Unity import. The game plays it through the sound
effect library: entry `WandFire` (id 0) in `Assets/Audio/Resources/SfxLibrary.asset`,
currently volume `0.722`, speed `1.14`, pitch `0.88`. These values are edited in
**Tools > Wizard Duel > 효과음 패널**. They change the in-game playback only; the
edited WAV and its source-preserving edit record above remain unchanged.

# MIDI Page Turner

With the original project by jacbz, 
I made and still make some customization to hopefully make this app usable with any MIDI device sending MIDI Note on/off or Control Change messages.
<p align="center">
  <img src="https://github.com/Davdavttm/MidiPageTurner/blob/master/app_overview.png?raw=true" width="400px"/>
</p>

## Getting started

1. Connect your Device to your Windows tablet using USB or Bluetooth MIDI
2. Press "Start". The indicator should turn green.

## Configuring

1. For both inputs, click "Capture MIDI Event", then press the key on your MIDI device you want to turn page forward/backwards with. (Or keep previously saved settings)
2. Select the keyboard key used to turn page forward/backward in your note viewer app
3. Switch to your note viewer app

## Planned

1. Implement debouncing for analog inputs
2. Implement "Invert function" feature (is by now just a checkbox with no function)
2. Save settings on configured "turn page" key presses
3. Implement configurable repeated key presses for flexibility (to e.g. scroll only half a page)
4. Implement option to simulate touch input drag as page turn/scroll? option
5. Implement option to simulate repeated mouse wheel up/down as a page turn/scroll? option

## License

Distributed under the Apache-2.0 License. See the LICENSE file for more information.
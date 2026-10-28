# Keys

How Winland names keys: the XKB keycode, the Windows scan code and the evdev code of each key a bind can name. Honored behavior is in [behavior.md](behavior.md), and terms are defined in [GLOSSARY.md](../GLOSSARY.md).

## Winland notes

`code:N` is the XKB keycode, which is the evdev keycode plus 8. It names a physical key, so it does not follow the active layout. A bare number above 9 in a bind is the same keycode, so `bind = SUPER, 36, exec, kitty` is `code:36`. `0` to `9` are the digit keys. A config is resolved when it is read. Character names such as `├ñ` and `adiaeresis` follow the layout that is active when the key is pressed. A virtual-key alias such as `bracketleft` is resolved against the layout active when the config is read.

Windows scan codes below are set 1. Extended means the extended-key flag. Pause uses the same scan as num lock; the virtual key tells them apart. The synthetic left control of an AltGr press (scan `0x21D`, or left control with the extended flag) is not a key in this table.

| key | scan | extended | evdev | code |
| --- | --- | --- | --- | --- |
| Escape | 0x01 | no | 1 | 9 |
| 1 | 0x02 | no | 2 | 10 |
| 2 | 0x03 | no | 3 | 11 |
| 3 | 0x04 | no | 4 | 12 |
| 4 | 0x05 | no | 5 | 13 |
| 5 | 0x06 | no | 6 | 14 |
| 6 | 0x07 | no | 7 | 15 |
| 7 | 0x08 | no | 8 | 16 |
| 8 | 0x09 | no | 9 | 17 |
| 9 | 0x0A | no | 10 | 18 |
| 0 | 0x0B | no | 11 | 19 |
| Q | 0x10 | no | 16 | 24 |
| W | 0x11 | no | 17 | 25 |
| E | 0x12 | no | 18 | 26 |
| R | 0x13 | no | 19 | 27 |
| T | 0x14 | no | 20 | 28 |
| Y | 0x15 | no | 21 | 29 |
| U | 0x16 | no | 22 | 30 |
| I | 0x17 | no | 23 | 31 |
| O | 0x18 | no | 24 | 32 |
| P | 0x19 | no | 25 | 33 |
| A | 0x1E | no | 30 | 38 |
| S | 0x1F | no | 31 | 39 |
| D | 0x20 | no | 32 | 40 |
| F | 0x21 | no | 33 | 41 |
| G | 0x22 | no | 34 | 42 |
| H | 0x23 | no | 35 | 43 |
| J | 0x24 | no | 36 | 44 |
| K | 0x25 | no | 37 | 45 |
| L | 0x26 | no | 38 | 46 |
| Z | 0x2C | no | 44 | 52 |
| X | 0x2D | no | 45 | 53 |
| C | 0x2E | no | 46 | 54 |
| V | 0x2F | no | 47 | 55 |
| B | 0x30 | no | 48 | 56 |
| N | 0x31 | no | 49 | 57 |
| M | 0x32 | no | 50 | 58 |
| F1 | 0x3B | no | 59 | 67 |
| F2 | 0x3C | no | 60 | 68 |
| F3 | 0x3D | no | 61 | 69 |
| F4 | 0x3E | no | 62 | 70 |
| F5 | 0x3F | no | 63 | 71 |
| F6 | 0x40 | no | 64 | 72 |
| F7 | 0x41 | no | 65 | 73 |
| F8 | 0x42 | no | 66 | 74 |
| F9 | 0x43 | no | 67 | 75 |
| F10 | 0x44 | no | 68 | 76 |
| F11 | 0x57 | no | 87 | 95 |
| F12 | 0x58 | no | 88 | 96 |
| Space | 0x39 | no | 57 | 65 |
| Tab | 0x0F | no | 15 | 23 |
| Return | 0x1C | no | 28 | 36 |
| BackSpace | 0x0E | no | 14 | 22 |
| Shift_L | 0x2A | no | 42 | 50 |
| Shift_R | 0x36 | no | 54 | 62 |
| Control_L | 0x1D | no | 29 | 37 |
| Alt_L | 0x38 | no | 56 | 64 |
| KP_0 | 0x52 | no | 82 | 90 |
| KP_1 | 0x4F | no | 79 | 87 |
| KP_2 | 0x50 | no | 80 | 88 |
| KP_3 | 0x51 | no | 81 | 89 |
| KP_4 | 0x4B | no | 75 | 83 |
| KP_5 | 0x4C | no | 76 | 84 |
| KP_6 | 0x4D | no | 77 | 85 |
| KP_7 | 0x47 | no | 71 | 79 |
| KP_8 | 0x48 | no | 72 | 80 |
| KP_9 | 0x49 | no | 73 | 81 |
| KP_Multiply | 0x37 | no | 55 | 63 |
| KP_Subtract | 0x4A | no | 74 | 82 |
| KP_Add | 0x4E | no | 78 | 86 |
| KP_Decimal | 0x53 | no | 83 | 91 |
| Scroll_Lock | 0x46 | no | 70 | 78 |
| Num_Lock | 0x45 | no | 69 | 77 |
| Pause | 0x45 | no | 119 | 127 |
| Up | 0x48 | yes | 103 | 111 |
| Down | 0x50 | yes | 108 | 116 |
| Left | 0x4B | yes | 105 | 113 |
| Right | 0x4D | yes | 106 | 114 |
| Home | 0x47 | yes | 102 | 110 |
| End | 0x4F | yes | 107 | 115 |
| Prior | 0x49 | yes | 104 | 112 |
| Next | 0x51 | yes | 109 | 117 |
| Insert | 0x52 | yes | 110 | 118 |
| Delete | 0x53 | yes | 111 | 119 |
| Print | 0x37 | yes | 99 | 107 |
| Menu | 0x5D | yes | 127 | 135 |
| KP_Enter | 0x1C | yes | 96 | 104 |
| KP_Divide | 0x35 | yes | 98 | 106 |
| Control_R | 0x1D | yes | 97 | 105 |
| Alt_R | 0x38 | yes | 100 | 108 |
| Super_L | 0x5B | yes | 125 | 133 |
| Super_R | 0x5C | yes | 126 | 134 |

F13 through F24 have no single set-1 scan code in this table. Enter is `code:36`. `code:28` is the T position, not Enter.

# Assets

The icon picker's (IconPicker.cs) symbols and emoji, made by `tools/build_icons.py`:

- **DriftIcons.ttf**: 49 icons from [Ionicons](https://ionic.io/ionicons), the set Arc uses, in its filled
  style, as a font ("Drift Icons"). Their characters start at U+F0000, apart from the Segoe Fluent Icons
  characters chosen before Drift had its own. Their names, for search, are in `Core/PickerIcons.Symbols.cs`.
- **emoji.txt**: the emoji, a line each (group, emoji, the words they're found by), from GitHub's
  [gemoji](https://github.com/github/gemoji), the list Arc uses. Up to Emoji 15.0, which Windows 11 draws,
  without country flags (Windows shows those as letters).

To change them, edit the list in `tools/build_icons.py` and run it with fontTools installed:

```
python tools/build_icons.py <ionicons>/dist/svg <gemoji>/db/emoji.json Drift
```

## Licences

Ionicons: MIT License, Copyright (c) 2015-present Ionic (http://ionic.io/).

gemoji: MIT License, Copyright (c) 2019 GitHub, Inc.

Both under these terms:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

# Hypersonic
A music streaming server that provides a subset of the Subsonic API.

[![Build status](https://ci.appveyor.com/api/projects/status/wijhgcm3dd32extk/branch/default?svg=true)](https://ci.appveyor.com/project/carlreinke/hypersonic/branch/default)

## Features

* Tag-based browsing
* Transcoding to Opus (best quality) and MP3 (best compatibility)
* Server-applied ReplayGain
* Playlists
* Jukebox

## Supported Clients

To use Hypersonic, you will need a Subsonic client.  Some features of supported clients may not be supported.

* [Aurial] (web)
* [DSub] (Android)
* [Ultrasonic] (Android)

Hypersonic does not provide a web interface by default.  One can be added by installing a supported web client into a directory named `ui` that is in the same directory as `Hypersonic.dll`.  The web client will be served on the same port as the API server.

## Dependencies

* .NET core 2.1
* ffmpeg (including ffprobe and ffplay)

On Linux, the ffmpeg package needs to be installed.  On Windows, the [ffmpeg] executables need to be installed in the same directory as `Hypersonic.dll`.

## Getting Started

Add a music library, add a user, and start the server:

```
dotnet Hypersonic.dll library add Music path/to/music/
dotnet Hypersonic.dll user add guest --guest
dotnet Hypersonic.dll serve --bind 0:4040 --bind [::]:4040
```

The API server is now running on port 4040.  Configure your client of choice and off you go.  Guest users can log in with any password.

Music libraries are scanned when the server starts and every 24 hours thereafter.


[Aurial]: https://github.com/shrimpza/aurial
[DSub]: https://github.com/daneren2005/Subsonic
[Ultrasonic]: https://github.com/ultrasonic/ultrasonic
[ffmpeg]: https://ffmpeg.org/download.html

# StreamOverlay

Mortar's stream overlay for Lethal Company: crew, quota and moon state, for OBS.

- Who needs it: everyone in the lobby.
- Config: none. Mortar installs it while its stream overlay setting is on and removes it when the setting is off.
- OBS: a Browser source at the URL Mortar copies, `file:///…/overlay/index.html?port=8123&token=…`. The page reads
  `http://127.0.0.1:8123/state`.

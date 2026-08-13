THERES 4 MAIN TABS WHEN SAYING SETTINGS ALWAYS SAY IF ITS IN PLAYER, GAMEPLAY, ENEMIES, or WORLD

Blender is driven through the official Blender Lab "MCP" extension (lab_blender_org/mcp) listening on 127.0.0.1:9876. Its wire protocol is a NUL-terminated JSON request, {"type":"execute","code":"...","strict_json":<bool>}, answered by a NUL-terminated JSON response. The sandbox has no implicit imports, so always import bpy in the code you send. The uvx blender-mcp entry in .mcp.json is an unrelated third-party PyPI package that speaks an incompatible dialect on the same port; when its tools fail to register, that is the config being wrong, not Blender being down.

Never report a service as hung, down, or broken based on a probe that did not follow that service's own protocol. Read the protocol first, then probe, then diagnose.

Always commit after work. Say the commit in output always.

Do not ever leave designer-facing tuning values hardcoded in scripts.

Put all designer-facing tuning fields on DuneVectorRuntimeSettings and author their values in Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset. Do not create separate tuning ScriptableObjects unless the user explicitly requests one.

All ScriptableObjects should live in Assets/DuneVector/ScriptableObjects.

All saving and persistent memory should be done to a .dat file.

When giving Unity Editor directions, always state the exact tab or window first, followed by the section or component containing the control.

Do not use computer use for verification. Use computer use only when it is needed to build.

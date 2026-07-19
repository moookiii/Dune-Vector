Always commit after work. Say the commit in output always.

Do not ever leave designer-facing tuning values hardcoded in scripts.

Put all designer-facing tuning fields on DuneVectorRuntimeSettings and author their values in Assets/DuneVector/ScriptableObjects/Dune Vector Runtime Settings.asset. Do not create separate tuning ScriptableObjects unless the user explicitly requests one.

All ScriptableObjects should live in Assets/DuneVector/ScriptableObjects.

All saving and persistent memory should be done to a .dat file.

When giving Unity Editor directions, always state the exact tab or window first, followed by the section or component containing the control.

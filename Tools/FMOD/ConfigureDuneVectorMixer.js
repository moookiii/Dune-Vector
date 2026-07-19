// Recreates the useful top-level routing from the July FMOD mix profile in the
// Dune Vector FMOD project. Run through FMOD Studio's -script command-line flag.
(function configureDuneVectorMixer() {
    var masterBus = studio.project.workspace.mixer.masterBus;

    // These are the original July-routed buses created for Dune Vector. A later
    // FMOD authoring pass created same-named duplicates, which prevents builds.
    var canonicalMusicBusId = "{0aa4bdaf-179f-484c-828c-538b1c1530fc}";
    var canonicalSfxBusId = "{9dfbfea9-d0e4-45c8-b168-c84ecff22646}";
    var duplicateMusicBusId = "{3a4ffccb-da31-43f1-b5fa-4a459697e730}";
    var duplicateSfxBusId = "{1fa45e6e-c2c4-428d-8756-831853642202}";

    function getOrCreateBus(path, name) {
        var bus = studio.project.lookup(path);
        if (!bus) {
            bus = studio.project.create("MixerGroup");
            bus.name = name;
            bus.output = masterBus;
            console.log("Created " + path);
        }
        return bus;
    }

    var musicBus = studio.project.lookup(canonicalMusicBusId) || getOrCreateBus("bus:/Music", "Music");
    var sfxBus = studio.project.lookup(canonicalSfxBusId) || getOrCreateBus("bus:/SFX", "SFX");

    var musicEvent = studio.project.lookup("event:/Shadows on the Mesa");
    if (!musicEvent) {
        throw new Error("Could not find event:/Shadows on the Mesa");
    }

    musicEvent.mixerInput.output = musicBus;
    var damageEvent = studio.project.lookup("event:/Drone_Damage");
    if (!damageEvent) {
        throw new Error("Could not find event:/Drone_Damage");
    }
    damageEvent.mixerInput.output = sfxBus;

    var duplicateMusicBus = studio.project.lookup(duplicateMusicBusId);
    if (duplicateMusicBus && duplicateMusicBus !== musicBus) {
        studio.project.deleteObject(duplicateMusicBus);
        console.log("Removed duplicate bus:/Music");
    }
    var duplicateSfxBus = studio.project.lookup(duplicateSfxBusId);
    if (duplicateSfxBus && duplicateSfxBus !== sfxBus) {
        studio.project.deleteObject(duplicateSfxBus);
        console.log("Removed duplicate bus:/SFX");
    }

    studio.project.save();
    console.log("Routed music and drone damage events to their mixer buses and saved the project.");
}());

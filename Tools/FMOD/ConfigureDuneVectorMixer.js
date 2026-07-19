// Recreates the useful top-level routing from the July FMOD mix profile in the
// Dune Vector FMOD project. Run through FMOD Studio's -script command-line flag.
(function configureDuneVectorMixer() {
    var masterBus = studio.project.workspace.mixer.masterBus;

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

    var musicBus = getOrCreateBus("bus:/Music", "Music");
    getOrCreateBus("bus:/SFX", "SFX");

    var musicEvent = studio.project.lookup("event:/Shadows on the Mesa");
    if (!musicEvent) {
        throw new Error("Could not find event:/Shadows on the Mesa");
    }

    musicEvent.mixerInput.output = musicBus;
    studio.project.save();
    console.log("Routed Shadows on the Mesa to bus:/Music and saved the project.");
}());

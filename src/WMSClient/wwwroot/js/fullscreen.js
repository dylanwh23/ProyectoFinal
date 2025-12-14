window.fullscreenHelper = {
    toggleFullscreen: function(elementId) {
        const element = document.getElementById(elementId);
        if (!element) return;

        if (!document.fullscreenElement) {
            element.requestFullscreen().catch(err => {
                console.error(`Error requesting fullscreen: ${err.message}`);
            });
        } else {
            if (document.exitFullscreen) {
                document.exitFullscreen();
            }
        }
    }
};

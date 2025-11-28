// --- Configuración ---
const HISTORY_BUFFER_SIZE = 600; // Cantidad de frames a cargar al pausar (aprox 1 min si son 10fps)
const SIGNAL_TIMEOUT_SECONDS = 10;

// --- Elementos ---
const imgDisplay = document.getElementById('video-display');
const btnToggle = document.getElementById('btn-toggle');
const btnIcon = btnToggle.querySelector('i');
const slider = document.getElementById('scrubber');
const badge = document.getElementById('live-badge');
const loader = document.getElementById('loader');
const noSignalOverlay = document.getElementById('no-signal');
const lblCurrent = document.getElementById('lbl-current');
const lblStart = document.getElementById('lbl-start');

// --- Estado ---
let cameraIP = null;
let isLive = true;
let framesBuffer = [];
let watchdogInterval = null;

// --- Inicio ---
document.addEventListener('DOMContentLoaded', () => {
    const params = new URLSearchParams(window.location.search);
    cameraIP = params.get('cam');

    if (!cameraIP) { alert("Falta IP"); return; }

    goLive();

    btnToggle.addEventListener('click', togglePlayPause);
    slider.addEventListener('input', onScrub);
});

function goLive() {
    isLive = true;
    framesBuffer = [];
    
    badge.className = 'status-badge status-live';
    badge.innerText = 'EN VIVO';
    btnIcon.className = 'bi bi-pause-fill';
    
    slider.disabled = true;
    slider.value = 100;
    
    lblCurrent.innerText = "Tiempo Real";
    lblStart.innerText = "";
    
    loader.style.display = 'none';
    noSignalOverlay.style.display = 'none';
    
    imgDisplay.src = `/api/stream/${cameraIP}?t=${Date.now()}`;
    
    startWatchdog();
}

async function goPause() {
    isLive = false;
    stopWatchdog();
    noSignalOverlay.style.display = 'none';
    
    loader.style.display = 'block';
    btnIcon.className = 'bi bi-play-fill';
    imgDisplay.src = ""; // Cortar stream
    badge.className = 'status-badge status-playback';
    badge.innerText = 'BUFFER';

    await loadHistoryBuffer();
    
    loader.style.display = 'none';
}

function togglePlayPause() {
    if (isLive) goPause();
    else goLive();
}

// --- LOGICA HISTORIAL CORREGIDA ---
async function loadHistoryBuffer() {
    try {
        // CAMBIO: Pedimos los últimos N archivos (Buffer Circular)
        const url = `/api/buffer/${cameraIP}?count=${HISTORY_BUFFER_SIZE}`;
        const res = await fetch(url);
        
        if (!res.ok) throw new Error("Sin imágenes recientes");
        
        const data = await res.json();
        framesBuffer = data.files || [];

        if (framesBuffer.length > 0) {
            slider.disabled = false;
            slider.min = 0;
            slider.max = framesBuffer.length - 1;
            slider.value = framesBuffer.length - 1; // Ir al final
            
            lblStart.innerText = `-${framesBuffer.length} frames`;
            showFrame(framesBuffer.length - 1);
        } else {
            lblCurrent.innerText = "Buffer vacío";
        }

    } catch (e) {
        console.error(e);
        lblCurrent.innerText = "Error cargando buffer";
    }
}

function onScrub(e) {
    if (isLive) return;
    showFrame(parseInt(e.target.value));
}

function showFrame(index) {
    if (!framesBuffer[index]) return;
    
    const filePath = framesBuffer[index];
    // encodeURIComponent es obligatorio para rutas de Windows
    imgDisplay.src = `/api/frame/${cameraIP}?file=${encodeURIComponent(filePath)}`;
    
    lblCurrent.innerText = `Frame -${framesBuffer.length - index}`;
}

// --- WATCHDOG ---
function startWatchdog() {
    stopWatchdog();
    watchdogInterval = setInterval(async () => {
        try {
            const res = await fetch(`/api/camaras/health/${cameraIP}`);
            if (res.ok) {
                const data = await res.json();
                if (data.secondsAgo > SIGNAL_TIMEOUT_SECONDS) {
                    noSignalOverlay.style.display = 'flex';
                } else {
                    noSignalOverlay.style.display = 'none';
                }
            }
        } catch (e) { }
    }, 2000);
}

function stopWatchdog() {
    if (watchdogInterval) clearInterval(watchdogInterval);
}
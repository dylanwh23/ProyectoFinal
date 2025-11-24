// --- Elementos del DOM ---
const titleElement = document.getElementById("titulo-camara");
const liveStreamImg = document.getElementById("live-stream-img");
const btnStart = document.getElementById("btnStart");
const btnStop = document.getElementById("btnStop");
const btnFrame = document.getElementById("btnFrame");
const historySlider = document.getElementById("history-slider");

// --- Variables de estado ---
let cameraName = null;
let isStreaming = false;
let fileHistory = [];
let currentHistoryId = null; // ID del historial congelado

// --- Inicialización ---
async function iniciarVisor() {
    const path = window.location.pathname;
    const params = new URLSearchParams(window.location.search);
    cameraName = params.get("cam") || path.substring(1).replace(/\/$/, "");

    if (!cameraName) {
        titleElement.textContent = "Navega a /camara1 o usa ?cam=camara1";
        btnStart.disabled = true;
        btnStop.disabled = true;
        btnFrame.disabled = true;
        historySlider.disabled = true;
        return;
    }

    btnStart.textContent = "Ver en Vivo";
    btnStop.textContent = "Detener";
    btnFrame.textContent = "Último Frame";

    iniciarStream();
}

// --- Congelar el historial ---
async function freezeHistory() {
    if (!cameraName) return;
    if (!isStreaming && currentHistoryId) {
        return; // Ya estamos en modo historial
    }

    console.log("Congelando historial...");
    detenerStream();
    titleElement.textContent = `Viendo: ${cameraName} (Congelando historial...)`;
    historySlider.disabled = true;

    try {
        const response = await fetch(`/api/history/freeze/${cameraName}`);
        if (!response.ok) throw new Error("No se pudo congelar el historial");

        const snapshot = await response.json();

        currentHistoryId = snapshot.historyId;
        fileHistory = snapshot.files;

        if (fileHistory.length > 0) {
            historySlider.max = fileHistory.length - 1;
            historySlider.disabled = false;
            console.log(`Historial congelado: ${currentHistoryId} con ${fileHistory.length} archivos.`);
            showFrozenFrame(historySlider.max);
        } else {
            titleElement.textContent = `Viendo: ${cameraName} (No se encontró historial)`;
        }

    } catch (error) {
        console.error("Error congelando historial:", error);
        titleElement.textContent = `Viendo: ${cameraName} (Error al cargar historial)`;
    }
}

// --- Mostrar un frame del historial congelado ---
function showFrozenFrame(index) {
    if (!currentHistoryId || isStreaming) return;

    const fileIndex = parseInt(index, 10);
    const fileName = fileHistory[fileIndex];
    if (!fileName) return;

    liveStreamImg.src = `/api/frame/${cameraName}?file=${fileName}&historyId=${currentHistoryId}&t=${Date.now()}`;
    titleElement.textContent = `Viendo: ${cameraName} (Historial: ${fileName})`;
    historySlider.value = fileIndex;
}

// --- MODIFICADO: Iniciar el stream en vivo ---
function iniciarStream() {
    if (!cameraName) return;

    // --- NUEVO: Limpiar el historial anterior ---
    if (currentHistoryId) {
        console.log(`Solicitando borrado de historial: ${currentHistoryId}`);
        // Enviamos la petición de borrado. No necesitamos 'await',
        // que se borre en segundo plano.
        fetch(`/api/history/cleanup/${cameraName}/${currentHistoryId}`, {
            method: 'DELETE'
        });
    }
    // --- FIN DE LA MODIFICACIÓN ---

    isStreaming = true;
    currentHistoryId = null; // Salir del modo historial
    fileHistory = [];

    liveStreamImg.src = `/api/stream/${cameraName}?t=${Date.now()}`;
    titleElement.textContent = `Viendo: ${cameraName} (En Vivo)`;

    // Dejamos la barra habilitada y al final
    historySlider.disabled = false;
    historySlider.max = 1;
    historySlider.value = 1;
}

// --- Detener el stream ---
function detenerStream() {
    isStreaming = false;
    liveStreamImg.src = ""; // Quita la fuente de la imagen

    // Habilitar el slider para que el usuario PUEDA usarlo
    historySlider.disabled = false;

    if (!currentHistoryId) {
        titleElement.textContent = `Viendo: ${cameraName} (Pausado - Mueva la barra para cargar historial)`;
    }
}

// --- Ver el último frame estático ---
async function verUltimoFrame() {
    await freezeHistory();
    if (fileHistory.length > 0) {
        showFrozenFrame(historySlider.max);
    }
}


// --- Event Listeners ---
btnStart.onclick = iniciarStream;
btnPause.onclick = verUltimoFrame;

historySlider.addEventListener("input", async (e) => {
    if (isStreaming) {
        await freezeHistory();
    }

    // Si 'freezeHistory' falló (ej. 0 archivos), 'currentHistoryId' será null
    if (currentHistoryId) {
        showFrozenFrame(e.target.value);
    }
});

// En app.js, añade esta nueva función:

async function loadEventHistoryLocal(startTime, endTime) {
    if (!cameraName) return;

    detenerStream();
    titleElement.textContent = `Viendo: ${cameraName} (Cargando evento...)`;
    historySlider.disabled = true;

    // --- LÓGICA DE HORA LOCAL ---
    // Formateamos la fecha a ISO pero SIN la 'Z'
    // Esto produce: 2025-11-09T21:30:00
    const formatLocalISO = (dt) => {
        const pad = (n) => n.toString().padStart(2, '0');
        return `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}T${pad(dt.getHours())}:${pad(dt.getMinutes())}:${pad(dt.getSeconds())}`;
    };

    const startTimeISO = formatLocalISO(startTime);
    const endTimeISO = formatLocalISO(endTime);

    try {
        // Llamamos al NUEVO endpoint "-local"
        const response = await fetch(`/api/history/freeze-by-range-local/${cameraName}?startTime=${startTimeISO}&endTime=${endTimeISO}`);

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || "No se pudo cargar el historial del evento local.");
        }

        const snapshot = await response.json();

        currentHistoryId = snapshot.historyId;
        fileHistory = snapshot.files;

        if (fileHistory.length > 0) {
            historySlider.max = fileHistory.length - 1;
            historySlider.disabled = false;
            console.log(`Historial de evento local cargado: ${currentHistoryId} con ${fileHistory.length} archivos.`);
            showFrozenFrame(0);
            historySlider.value = 0;
        } else {
            titleElement.textContent = `Viendo: ${cameraName} (No se encontraron imágenes para el evento)`;
        }

    } catch (error) {
        console.error("Error cargando historial de evento local:", error);
        titleElement.textContent = `Viendo: ${cameraName} (Error al cargar evento)`;
        iniciarStream(); // Volver a "En Vivo" si falla
    }
}

// Iniciar todo
iniciarVisor();
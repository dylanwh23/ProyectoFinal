// Helper para conectar eventos cuando Blazor falla
window.blazorHelper = {
    _initialized: false,
    _components: {},
    
    initEvents: function (dotnetHelper, componentName) {
        console.log('🔧 Inicializando eventos para componente:', componentName);
        
        // Guardar referencia por nombre de componente
        this._components[componentName] = dotnetHelper;
        
        // Solo registrar el listener una vez
        if (this._initialized) {
            console.log('⚠️ Ya inicializado, solo actualizando referencia de', componentName);
            return;
        }
        
        this._initialized = true;
        
        // Capturar clicks en botones con data-blazor-action
        document.addEventListener('click', function(e) {
            const target = e.target.closest('[data-blazor-action]');
            if (target) {
                const action = target.getAttribute('data-blazor-action');
                const component = target.getAttribute('data-blazor-component');
                const cameraIp = target.getAttribute('data-camera-ip');
                const eventId = target.getAttribute('data-event-id');
                const param = cameraIp || eventId;
                console.log('🎯 Click detectado:', action, component, param);
                
                // Obtener la referencia correcta del componente
                const handler = window.blazorHelper._components[component];
                if (handler) {
                    handler.invokeMethodAsync('HandleAction', component, action, param)
                        .catch(err => console.error('Error llamando a Blazor:', err));
                } else {
                    console.error('❌ No hay handler registrado para:', component);
                }
                
                e.preventDefault();
                e.stopPropagation();
            }
        }, true);
        
        console.log('✅ Eventos inicializados');
    }
};

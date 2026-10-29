# Pulido de items — seguimiento

Documento vivo del pulido visual y mecánico del sistema de items (sin añadir items nuevos).
Cada sesión se apunta en el **Registro** y se tacha lo hecho en la **Lista**.

**Leyenda:** ~~tachado~~ ✅ = hecho · 🟡 = preparado o a medias (ver nota) · ⛔ = descartado · sin marca = pendiente

---

## Registro de sesiones

### Sesión 1 — 29-oct-2026 · Punto 0 + quick wins

**Hecho**
- Todos los bugs del punto 0, salvo la inmunidad a tu propia banana (⛔ decisión de diseño: tu banana **sí** te tumba).
- Quick wins: pop de la ItemBox, ruleta que frena con saltitos, la banana gira en el aire y se aplasta al caer, el resbalón patina y gira, la cáscara sale volando con un «¡PLAF!» y el «+10» flota sobre las estanterías (este último descartado en la sesión 2).
- Sistema de **huecos de sonido** (`SfxCue`): 10 sonidos en 9 campos repartidos por los scripts, todos vacíos y documentados abajo en «Sonidos pendientes».
- Bugs extra que salieron por el camino: el temporizador del extintor descontaba el doble y la física de la banana se peleaba con la red en los clientes.

**Cambios de comportamiento que hay que saber**
- **Los objetos se usan al SOLTAR el botón**, no al pulsarlo. Un toque rápido se siente igual que antes; si mantienes, sale la flecha de apuntado y el objeto se usa al soltar. Mientras apuntas no se puede cambiar de objeto ni interactuar, y si te golpean se cancela sin gastarlo.
- **Extintor:** su duración en `PF_ExtintorPickup` pasa de 15 a **7,5**. Antes el código restaba el tiempo dos veces por frame, así que 15 duraba 7,5 s de verdad. Ahora dura lo que pone el inspector y se siente igual que antes.
- **Banana:** la malla está ahora en un hijo `Visual` dentro de `PF_BananaTrap`. El NetworkTransform sincroniza la escala del root, así que sin ese hijo no se podía aplastar la malla.
- **Banana en clientes:** su Rigidbody pasa a cinemático fuera del servidor. Antes simulaba gravedad a la vez que el NetworkTransform la movía.

**Archivos tocados**
- Nuevos: `Scripts/Feedback/SfxCue.cs`, `FloatingText.cs`, `FlyAwayDebris.cs`
- Scripts: `ItemBox`, `ItemData`, `ItemDatabase`, `LootPoint`, `PlayerItemHandler`, `BananaEffect`, `BananaTrap`, `PlayerController`, `PlayerLoot`, `HUDPlayerPresenter`, `HUDView`, `Floater`, `VibrationManager`, `PickupWatch`
- Assets: `PF_BananaTrap.prefab` (hijo Visual), `PF_ExtintorPickup.prefab` (duración), `ITEM_Banana_New.asset` (giro en el aire)

**Verificación:** compila sin errores (dotnet y el propio Unity). **Sin probar todavía en partida**: ver «Cómo probarlo».

### Sesión 2 — 29-oct-2026 · Descartes

- ⛔ **«+10» en las estanterías:** sobra. Quitado de `LootPoint` y `PlayerLoot`. Se mantienen el cooldown por jugador y los huecos de sonido al saquear (#7 y #8), que ahora van por un RPC solo de sonido (`ServerNotifyLootGrabbed`).
- `FloatingText` se queda: lo sigue usando el «¡PLAF!» de la banana.

---

## Cómo probarlo (sesión 1)

1. **ItemBox:** cógela → se hincha y vuela encogiéndose hacia ti. A los 8 s (`Respawn Time`) reaparece creciendo con un rebote. Con el inventario lleno no se coge.
2. **Usar/apuntar:** un toque lanza al momento. Si mantienes, la flecha sale a los 0,12 s y el objeto se usa al soltar. Si te pegan mientras apuntas, no lo gastas.
3. **Banana:** voltea en el aire, cae derecha con aplastón y botecito. Al pisarla (también la tuya): patinas con el impulso que llevabas, das una vuelta, sale un «¡PLAF!» y la cáscara vuela.
4. **Estantería:** dos jugadores saqueando la misma estantería durante el x2 ya no se bloquean entre sí.
5. **Ruleta del HUD:** empieza rápida, frena, cada icono da un saltito y termina con la entrada de la caja.
6. **Extintor:** dura lo mismo que antes (unos 7,5 s).
7. **En red (host + cliente):** todo lo anterior también se ve en el cliente.

Valores para ajustar en el inspector: `PlayerController › Resbalón (plátano)`, `BananaTrap › Visual`, `ItemBox › Respawn / Pop`, `PlayerItemHandler › Aim Arrow Delay`.

---

## Sonidos pendientes

Todavía no hay audio. Cada hueco es un campo `SfxCue` en el inspector (clips, volumen, variación de pitch y 2D/3D). Mientras esté vacío no hace nada. Cuando lleguen los sonidos, solo hay que arrastrarlos al campo.

| # | Dónde se asigna | Campo | Cuándo suena | Qué debería sonar | Quién lo oye |
|---|---|---|---|---|---|
| 1 | `PF_BoxItem` › ItemBox | `Pickup Sfx` | Al recoger la caja | «Pop» de cartón que se abre + brillito corto | Todos, desde la caja |
| 2 | `PF_BoxItem` › ItemBox | `Respawn Sfx` | Al reaparecer la caja | «Bloop» suave, como algo que se infla | Todos, desde la caja |
| 3 | `ITEM_Banana_New` | `Use Sfx` | Al lanzar el plátano | «Fiu» de lanzamiento | Todos, desde el jugador |
| 4 | `ITEM_EnergyDrink` | `Use Sfx` | Al beber | Lata que se abre «pssst» + trago | Todos, desde el jugador |
| 5 | `PF_BananaTrap` › BananaTrap | `Land Sfx` | Cuando la cáscara cae al suelo | «Plaf» blandito y húmedo | Todos, desde la cáscara |
| 6 | `PF_BananaTrap` › BananaTrap | `Slip Sfx` | Cuando alguien resbala | Chirrido de patinazo «fiiiu» + golpe cómico | Todos, desde la víctima |
| 7 | Prefab del jugador › PlayerLoot | `Loot Grab Sfx` | Cada objeto saqueado | «Tink» corto / bolsa que se llena (suena seguido: muy corto) | Todos, desde la estantería |
| 8 | Prefab del jugador › PlayerLoot | `Loot Grab Double Sfx` | Igual, durante el evento x2 | Versión «dorada»: monedas, brillo. Si está vacío usa el #7 | Todos, desde la estantería |
| 9 | `PF_HUD_*` › HUDView | `Roulette Tick Sfx` | En cada icono de la ruleta | «Tic» seco de ruleta de feria (< 0,1 s). El código ya lo espacia y lo baja de pitch al frenar | UI (2D) |
| 10 | `PF_HUD_*` › HUDView | `Roulette Stop Sfx` | Cuando la ruleta se para | «Ding» / campanita alegre | UI (2D) |

> El juego tiene cámara cenital lejana: los sonidos de mundo funcionan mejor con `Spatial Blend` bajo (0–0,3).

---

## Lista de ideas

### 0. Cosas rotas o a medias
- ~~Las ItemBox nunca vuelven a salir~~ ✅ `Respawn Time` (8 s por defecto; 0 = no vuelve)
- ~~El aviso «Item box obtenida» sale por `OnGUI`~~ ✅ quitado; el aviso ya lo da la ruleta del HUD
- ~~Usar un objeto y apuntar no casan~~ ✅ mantener = apuntar, soltar = usar
- ⛔ ~~Tu propia banana te puede tumbar~~ — se queda así por diseño
- ~~El cooldown de `LootPoint` es compartido~~ ✅ ahora va por jugador
- ~~Codificación rota (`�`) en LootPoint, BananaTrap, PlayerItemHandler~~ ✅ (también PickupWatch, VibrationManager y PlayerLoot)
  - Quedan en Latin-1, fuera del sistema de items: `eventsManager`, `eventSpeedUpTimer`, `PauseMenu`, `MatchTimer`
- ~~Código muerto: `Start()` en `ItemDatabase`~~ ✅
- ~~Logs en cada uso: `Floater` y `VibrationManager`~~ ✅ (y los del extintor en PlayerController)
- ~~`PickupWatch` no está en red~~ ✅ detecta al PlayerController, solo vibra el mando de quien lo coge y se esconde en vez de destruirse
- ~~(extra) El temporizador del extintor descontaba el doble y había un bloque de código inalcanzable~~ ✅
- ~~(extra) La física de la banana se peleaba con el NetworkTransform en los clientes~~ ✅

### 1. Base común: el «feel» de cada item
- 🟡 Añadir a `ItemData`: ~~SFX al usar~~ ✅ (hueco). Pendiente: color/rareza, SFX al recoger, VFX al usar, perfil de vibración, frase para el HUD, silueta para la ruleta
- 🟡 RPC de feedback central: `ItemUsedRpc` ya reproduce el `Use Sfx` en todos los clientes. Falta el VFX
- Vibración por jugador con perfiles (recoger / usar / tumbado / x2)
- 🟡 ~~Audio con variación de pitch~~ ✅ (`SfxCue` ya lo trae; faltan los audios)
- Hit-stop de 2–4 frames al pisar una banana y al chocar con el flotador

### 2. ItemBox
- ~~Que reaparezca tras X segundos~~ ✅
- «Fantasma» o anillo que se llena mientras espera para reaparecer
- Feedback de inventario lleno (rebote / negar con la cabeza + buzz)
- Reparto por peso y según la posición (rubber-band)
- Fase de giro sincronizada entre todas las cajas
- Anticipación al acercarse (más giro, rim light)
- ~~Pop al recoger: squash & stretch y la caja «tragada» hacia el jugador~~ ✅ · 🟡 confeti: hay hueco `Pickup Vfx Prefab`, falta el VFX
- ~~(extra) Pop al reaparecer, con rebote~~ ✅ · 🟡 hueco `Respawn Vfx Prefab`
- Sombra o decal en el suelo
- «?» girando dentro / tapa que se entreabre

### 3. Ruleta del HUD
- ~~Que frene con ease-out~~ ✅ · 🟡 ~~«tic» con el pitch cambiando~~ (hueco listo; el pitch baja al frenar)
- ~~Saltito en cada icono~~ ✅ · el punch final ya lo hacía la entrada de la caja · 🟡 «ding» final (hueco)
- Flash del color de rareza al pararse + vibración
- Pulsar «usar» durante la ruleta la salta
- Siluetas durante la ruleta, revelación al final

### 4. Sostener y usar el objeto
- 🟡 Tap = lanzar, ~~mantener = apuntar~~ ✅. Pendiente: **arco de trayectoria** al mantener y **soltar detrás** con el stick hacia atrás
- Pop de escala del objeto en la mano + balanceo idle; el viejo sale lanzado al cambiar
- Sonido al cambiar de objeto + slot del HUD que se mueve a la vez
- Que los rivales vean qué lleva cada uno en la mano

### 5. Banana
- ~~Que gire en el aire~~ ✅ · rastro amarillo corto
- ~~Aterrizaje con squash + minibote~~ ✅ · 🟡 «plaf» (hueco)
- Parpadeo/translúcida mientras no está armada + «clic» al armarse
- ⛔ ~~Inmunidad del lanzador~~ (decisión de diseño)
- Contorno o highlight tenue
- Parpadeo en los últimos 3 s antes de desaparecer
- Que un puñetazo la mande volando
- ~~Resbalón conservando el impulso + vuelta~~ ✅ · ~~cáscara saliendo disparada~~ ✅ · pendiente: un objeto saltando de la bolsa
- ~~«¡SPLAT!» sobre la víctima~~ ✅ (sale «¡PLAF!», se cambia en `Slip Text`)
- Guardar quién la lanzó → kill-feed «X ha pisado la banana de Y» y estadísticas

### 6. Bebida energética
- Que los demás lo vean (NetworkVariable del buff → aura, rastro, chispas)
- Animación de trago, lata vacía al suelo · 🟡 sonido de lata (hueco `Use Sfx`)
- Anillo de duración + aviso en el último segundo
- Dash con bebida distinto (FOV, whoosh)

### 7. Extintor
- ~~(bug) La duración real era la mitad de la configurada~~ ✅
- Espuma hacia atrás, retroceso, sonido en bucle con pitch
- Indicador de dirección en el suelo
- Charcos de espuma que frenan o hacen resbalar a los demás
- Chisporroteo en el último segundo
- Pickup que gire y flote como la ItemBox

### 8. Flotador
- Inflado con squash elástico
- «Boing» + squash + anillo de impacto + hit-stop en cada rebote
- Desinflado al terminar
- ~~Quitar `Debug.Log`~~ ✅ · indicador de duración

### 9. LootPoint (estanterías)
- ~~Cooldown por jugador~~ ✅
- Ritmo que se acelera al mantener + pitch que sube con el combo
- La estantería se vacía de verdad (los meshes desaparecen)
- ⛔ ~~«+10» flotante, dorado en el x2~~ — se hizo en la sesión 1 y se quitó en la 2: sobra
- El objeto volando en arco hasta la bolsa
- Brillo dorado + partículas + contador de recogidas durante el x2
- Estantería vacía (polvo / oscurecida) y pop al rellenarse
- Vibración en cada recogida, más fuerte en la última
- 🟡 Sonido al saquear (huecos `Loot Grab Sfx` / `Loot Grab Double Sfx`)

### 10. Transversal
- Shader de contorno común para todo lo que se puede coger o usar
- Pooling de bananas y VFX (también las fuentes de audio de `SfxCue`)
- Estadísticas al final de la partida
- ~~Texto flotante sin Canvas Overlay~~ ✅ (`FloatingText` es TextMeshPro 3D, sin canvas)

---

## Prioridades (VS 13-nov)

1. ~~Bugs del punto 0~~ ✅
2. ~~Quick wins de «juice»: pop de caja, ruleta, aterrizaje y resbalón de la banana~~ ✅ (⛔ «+10» descartado)
3. **Siguiente:** feedback en red: que la bebida y el objeto en la mano se vean en todos los clientes
4. Lo más grande: arco de apuntado, charcos del extintor, estanterías que se vacían de verdad y reparto por posición

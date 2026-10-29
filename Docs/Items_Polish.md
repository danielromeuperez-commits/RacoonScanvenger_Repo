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

### Sesión 3 — 29-oct-2026 · Puntos 2, 3, 4 y 5 de la lista

**Hecho**
- **2. ItemBox:** anillo que se llena hasta que vuelve, «no-no» con el inventario lleno, reparto por peso según la posición en puntos, giro sincronizado entre cajas y pantallas, anticipación al acercarte, sombra en el suelo y «?» en las caras.
- **3. Ruleta:** siluetas mientras gira, destello del color del objeto al pararse, vibración del mando y se salta pulsando Usar.
- **4. Sostener y usar:** arco de puntos con la trayectoria real y anillo donde caerá, soltar detrás con el stick hacia atrás, pop y balanceo del objeto en la mano, se guarda a la espalda al cambiar y hueco de sonido al cambiar.
- **5. Banana:** rastro en el aire, parpadeo hasta armarse y «clic» con un anillo que late, parpadeo antes de caducar, el puñetazo la manda volando, se cae algo de la bolsa al resbalar, se guarda quién la lanzó y sale «¡AUTOGOL!» si era tuya.
- Nuevo `PlayerRumble`: vibra el mando de UN jugador (el suyo), no el último mando usado.
- Todo lo visual nuevo (anillos, sombra, arco de puntos, rastro) usa el kit de VFX que ya había (`FlatPiece` / `FlatMeshes` con `M_VFX_ToonFlat`), así va a juego con los demás efectos.

**Bug grave que salió por el camino**
- El objeto en la mano del plátano era **el propio prefab de red** `PF_BananaTrap` (Rigidbody, colliders, NetworkObject). Al equiparlo se creaba una copia con física pegada a la mano. Ahora `PlayerItemHandler` le quita scripts, física y colliders antes de enseñarlo.

**Cambios de comportamiento que hay que saber**
- **Mientras apuntas vas más despacio** (`Aim Move Multiplier` 0,6).
- **Stick a más de 120° de donde miras** (`Drop Behind Angle`) = soltar detrás: no te giras (el arco se vuelve naranja) y al soltar el plátano cae a tu espalda.
- **Reparto de las cajas:** el plátano sale más a quien va último (peso 1 → 1,6) y la bebida más a quien va primero (1,3 → 1). Se cambia en cada `ITEM_*` › `Box Weight Leader / Last`. Con todo a 1 vuelve a ser al azar puro.
- **El puñetazo también golpea plátanos:** al caer otra vez, el plátano pasa a ser «de» quien lo ha golpeado.
- **Al resbalar se cae el 15 % del loot sin vender** (`BananaTrap › Slip Loot Drop Fraction`; 0 = nada). Usa las mismas bolsitas que el golpe fuerte.

**Archivos tocados**
- Nuevo: `Scripts/Feedback/PlayerRumble.cs`
- Scripts: `ItemBox`, `ItemData`, `ItemEffect`, `BananaEffect`, `BananaTrap`, `PlayerItemHandler`, `PlayerController`, `PlayerLoot`, `HUDPlayerPresenter`, `HUDController`
- Assets: `PF_BoxItem`, `PF_BananaTrap` y los 3 `PF_Player*` (campo `Flat Material`), `ITEM_Banana_New` y `ITEM_EnergyDrink` (pesos, color, valores de soltar detrás)

**Verificación:** compila sin errores con dotnet. **Sin probar en partida.**

---

## Cómo probarlo (sesión 3)

1. **ItemBox:** al acercarte gira más rápido y crece. Todas las cajas giran a la vez, y también en el otro cliente. Al cogerla queda un anillo dorado en el suelo que se llena y late más rápido hasta que vuelve. Con el inventario lleno, la caja dice que no y solo tú ves «¡LLENO!». Tiene «?» en las caras y sombra.
2. **Ruleta:** giran siluetas oscuras y al parar se descubre el objeto con un destello de su color (amarillo el plátano, cian la bebida) y vibra el mando. Pulsar Usar a mitad la para al momento.
3. **Apuntar el plátano:** al mantener sale un arco de puntos que avanzan y un anillo donde caerá. Con el stick hacia atrás el arco se vuelve naranja, no te giras y el plátano cae detrás.
4. **Objeto en la mano:** entra con un pop y se balancea; al cambiar, el viejo salta a la espalda. El rival también lo ve. El plátano en la mano ya no lleva física.
5. **Plátano en el suelo:** en el aire deja un rastro amarillo. Al caer parpadea hasta que está armado; entonces hace pop y aparece un anillo que late. Los últimos 3 s parpadea. Un puñetazo lo manda volando y vuelve a caer como trampa. Si resbalas con el tuyo sale «¡AUTOGOL!» y se te cae una bolsita de loot.

> Si tenías abierto `PF_PlayerOpossum` en Prefab Mode mientras se editaba, comprueba que su `PlayerItemHandler` tiene `Flat Material = M_VFX_ToonFlat`: el autoguardado de Unity podría haberlo pisado.

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
| 11 | `PF_BoxItem` › ItemBox | `Inventory Full Sfx` | Al tocar la caja con el inventario lleno | «Bzzt» de error corto y gracioso | Todos, desde la caja |
| 12 | Prefab del jugador › PlayerItemHandler | `Switch Sfx` | Al cambiar de objeto | «Swish» de meter algo en la mochila y sacar otra cosa | Todos, desde el jugador |
| 13 | `PF_BananaTrap` › BananaTrap | `Arm Sfx` | Cuando la cáscara queda armada | «Clic» / «ping» corto y sutil | Todos, desde la cáscara |
| 14 | `PF_BananaTrap` › BananaTrap | `Kick Sfx` | Al darle un puñetazo a la cáscara | «Tonk» blando + «fiuu» de salir volando | Todos, desde la cáscara |

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
- 🟡 Añadir a `ItemData`: ~~SFX al usar~~ ✅ (hueco) · ~~color/rareza~~ ✅ (`Rarity Color`) · ~~silueta para la ruleta~~ ✅ (se tiñe el icono, sin sprite aparte). Pendiente: SFX al recoger, VFX al usar, perfil de vibración, frase para el HUD
- 🟡 RPC de feedback central: `ItemUsedRpc` ya reproduce el `Use Sfx` en todos los clientes. Falta el VFX
- 🟡 Vibración por jugador: ~~helper `PlayerRumble`~~ ✅ (ya vibra en la ruleta, el resbalón y el «¡LLENO!»). Pendiente: perfiles (recoger / usar / tumbado / x2)
- 🟡 ~~Audio con variación de pitch~~ ✅ (`SfxCue` ya lo trae; faltan los audios)
- Hit-stop de 2–4 frames al pisar una banana y al chocar con el flotador

### 2. ItemBox
- ~~Que reaparezca tras X segundos~~ ✅
- ~~Anillo que se llena mientras espera para reaparecer~~ ✅ (late más rápido al final) · «fantasma» translúcido pendiente (el material de VFX es opaco)
- ~~Feedback de inventario lleno: negar con la cabeza~~ ✅ + «¡LLENO!» solo para ti + vibración · 🟡 buzz (hueco #11) · rebote del jugador pendiente
- ~~Reparto por peso y según la posición (rubber-band)~~ ✅ pesos en cada `ITEM_*`
- ~~Fase de giro sincronizada entre todas las cajas~~ ✅ (reloj del servidor: igual en todos los clientes)
- ~~Anticipación al acercarse: más giro~~ ✅ + crece y late · rim light pendiente (necesita shader)
- ~~Pop al recoger: squash & stretch y la caja «tragada» hacia el jugador~~ ✅ · 🟡 confeti: hay hueco `Pickup Vfx Prefab`, falta el VFX
- ~~(extra) Pop al reaparecer, con rebote~~ ✅ · 🟡 hueco `Respawn Vfx Prefab`
- ~~Sombra en el suelo~~ ✅ (se encoge cuando la caja sube)
- ~~«?» en las caras~~ ✅ · tapa que se entreabre pendiente (la caja es un cubo primitivo)

### 3. Ruleta del HUD
- ~~Que frene con ease-out~~ ✅ · 🟡 ~~«tic» con el pitch cambiando~~ (hueco listo; el pitch baja al frenar)
- ~~Saltito en cada icono~~ ✅ · el punch final ya lo hacía la entrada de la caja · 🟡 «ding» final (hueco)
- ~~Flash del color de rareza al pararse + vibración~~ ✅
- ~~Pulsar «usar» durante la ruleta la salta~~ ✅ (el objeto ya es tuyo: si mantienes, además empiezas a apuntar)
- ~~Siluetas durante la ruleta, revelación al final~~ ✅

### 4. Sostener y usar el objeto
- ~~Tap = lanzar, mantener = apuntar~~ ✅ · ~~arco de trayectoria~~ ✅ (puntos que avanzan + anillo de caída) · ~~soltar detrás con el stick hacia atrás~~ ✅
- ~~Pop de escala del objeto en la mano + balanceo idle~~ ✅ · ~~el viejo se va al cambiar~~ ✅ (salta a la espalda, «a la mochila», en vez de salir lanzado: el objeto sigue siendo tuyo)
- 🟡 Sonido al cambiar de objeto (hueco #12) · ~~slot del HUD que se mueve a la vez~~ ✅ (ya lo hacía el HUD)
- ~~Que los rivales vean qué lleva cada uno en la mano~~ ✅ (ya se veía; arreglado el plátano en la mano, que llevaba física)

### 5. Banana
- ~~Que gire en el aire~~ ✅ · ~~rastro amarillo corto~~ ✅
- ~~Aterrizaje con squash + minibote~~ ✅ · 🟡 «plaf» (hueco)
- ~~Parpadeo mientras no está armada + «clic» al armarse~~ ✅ · 🟡 sonido (hueco #13)
- ⛔ ~~Inmunidad del lanzador~~ (decisión de diseño)
- ~~Highlight tenue~~ ✅ con un anillo que late en el suelo cuando está armada (un contorno de verdad necesitaría shader)
- ~~Parpadeo en los últimos 3 s antes de desaparecer~~ ✅
- ~~Que un puñetazo la mande volando~~ ✅ · 🟡 sonido (hueco #14)
- ~~Resbalón conservando el impulso + vuelta~~ ✅ · ~~cáscara saliendo disparada~~ ✅ · ~~loot saltando de la bolsa~~ ✅ (15 %)
- ~~«¡SPLAT!» sobre la víctima~~ ✅ (sale «¡PLAF!», se cambia en `Slip Text`)
- ~~Guardar quién la lanzó~~ ✅ · ~~«¡AUTOGOL!» si es tuya~~ ✅ · 🟡 kill-feed y estadísticas: el evento `BananaTrap.Slipped(víctima, lanzador)` ya existe; falta la UI

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
3. 🟡 Feedback en red: ~~el objeto en la mano lo ven todos~~ ✅ · **falta que la bebida se vea en todos los clientes**
4. 🟡 Lo más grande: ~~arco de apuntado~~ ✅ · ~~reparto por posición~~ ✅ · faltan los charcos del extintor y que las estanterías se vacíen de verdad

# Showcase VFX: tiempos para el montaje (demo reel)

Tiempo de grabación a 60 fps.

| Plano | Empieza | Dura |
|---|---|---|
| Intro | 00:00:00 | 2,5 s |
| Pasos (polvo y huellas) | 00:02:30 | 2,0 s |
| Carrera (estela de viento) | 00:04:30 | 2,2 s |
| Estelas por jugador + dash | 00:06:42 | 3,0 s |
| Golpe al aire | 00:09:42 | 1,5 s |
| Golpe ligero sobre el rival | 00:11:12 | 1,9 s |
| Golpe cargado + mareo | 00:13:06 | 3,1 s |
| Impacto estrella | 00:16:12 | 1,4 s |
| Cansancio | 00:17:36 | 2,2 s |
| Bebida energética | 00:19:48 | 2,8 s |
| Evento x2: flecha + lootpoint | 00:22:36 | 3,6 s |
| Fuegos artificiales | 00:26:12 | 2,8 s |
| UI: HUD + cinta + tarjeta + avisos | 00:28:00 | 6,2 s |
| Pompa de visión | 00:35:12 | 3,8 s |
| Mallas procedurales | 00:38:00 | 4,1 s |
| Gameplay: caja, plátano, apuntado y golpe | 00:43:06 | 6,4 s |
| Cierre | 00:49:30 | 2,2 s |
| Pantalla de resultado | 00:51:44 | 4,2 s |

Duración total: 00:55:56.

## Cues

| Tiempo | Cue |
|---|---|
| 00:01:00 | `A:Swing` |
| 00:01:30 | `B:Swing` |
| 00:04:42 | `A:Wind:Run` |
| 00:06:37 | `A:Wind:Off` |
| 00:06:54 | `A:Wind:Run` |
| 00:06:54 | `B:Wind:Run` |
| 00:08:36 | `A:Dash` |
| 00:08:45 | `B:Dash` |
| 00:09:18 | `A:Wind:Off` |
| 00:09:27 | `B:Wind:Off` |
| 00:09:30 | `A:Steps:On` |
| 00:09:33 | `B:Steps:On` |
| 00:10:06 | `A:Swing` |
| 00:11:31 | `A:Swing` |
| 00:11:38 | `B:HitLight:A` |
| 00:13:04 | `B:GetUp` |
| 00:13:09 | `A:Charge:0.7` |
| 00:13:57 | `A:SwingCharged` |
| 00:13:57 | `A:ChargeEnd` |
| 00:14:03 | `B:HitCharged:A` |
| 00:16:09 | `B:GetUp` |
| 00:16:13 | `B:Style:Star` |
| 00:16:26 | `A:Swing` |
| 00:16:34 | `B:HitLight:A` |
| 00:17:32 | `B:GetUp` |
| 00:17:38 | `B:Style:Burst` |
| 00:17:40 | `B:Tired:2.3` |
| 00:20:03 | `A:Energy:On` |
| 00:22:27 | `A:Energy:Off` |
| 00:22:42 | `Arrow:Show` |
| 00:22:57 | `Msg:SHOWCASE_UI:X2On` |
| 00:26:06 | `Msg:SHOWCASE_UI:X2Off` |
| 00:26:09 | `Arrow:Hide` |
| 00:26:12 | `Prop:Fireworks:On` |
| 00:28:57 | `Prop:Fireworks:Off` |
| 00:28:00 | `Prop:HUDDemo:On` |
| 00:28:00 | `Prop:HUD:On` |
| 00:29:18 | `Msg:SHOWCASE_UI:EventDouble` |
| 00:30:12 | `Msg:SHOWCASE_UI:Card` |
| 00:30:24 | `Prop:AvisoSaquear:On` |
| 00:30:27 | `Anim:AvisoSaquear:Visible:true` |
| 00:30:51 | `Anim:AvisoSaquear:Holding:true` |
| 00:30:51 | `Msg:SHOWCASE_UI:Fill:AvisoSaquear,0.45` |
| 00:31:18 | `Anim:AvisoSaquear:Grab` |
| 00:31:18 | `Msg:SHOWCASE_UI:Fill:AvisoSaquear,0.5` |
| 00:31:21 | `Msg:SHOWCASE_UI:CardHit` |
| 00:31:48 | `Anim:AvisoSaquear:Grab` |
| 00:31:51 | `Msg:SHOWCASE_UI:CardHit` |
| 00:32:12 | `Anim:AvisoSaquear:Visible:false` |
| 00:32:33 | `Prop:AvisoSaquear:Off` |
| 00:32:33 | `Prop:AvisoVender:On` |
| 00:32:36 | `Anim:AvisoVender:Visible:true` |
| 00:32:57 | `Anim:AvisoVender:Holding:true` |
| 00:32:57 | `Msg:SHOWCASE_UI:Fill:AvisoVender,0.9` |
| 00:33:24 | `Msg:SHOWCASE_UI:CardClear` |
| 00:33:51 | `Anim:AvisoVender:Sold` |
| 00:34:42 | `Anim:AvisoVender:Visible:false` |
| 00:35:06 | `Prop:AvisoVender:Off` |
| 00:35:06 | `Prop:HUDDemo:Off` |
| 00:35:07 | `Msg:SHOWCASE_UI:EventHide` |
| 00:35:07 | `Prop:HUD:Off` |
| 00:39:01 | `Prop:Gallery:On` |
| 00:41:07 | `Msg:Gallery:SetWire:On` |
| 00:43:03 | `Prop:Gallery:Off` |
| 00:43:06 | `Msg:SHOWCASE_UI:BoxSpawn:624.6,-0.55` |
| 00:43:09 | `A:Wind:Run` |
| 00:44:06 | `A:Wind:Off` |
| 00:44:06 | `Msg:SHOWCASE_UI:BoxPick` |
| 00:44:36 | `Msg:SHOWCASE_UI:AimBegin:1.6` |
| 00:46:24 | `Msg:SHOWCASE_UI:AimThrow` |
| 00:46:57 | `B:HitHeavy:A` |
| 00:48:00 | `Msg:SHOWCASE_UI:AimClear` |
| 00:49:24 | `B:GetUp` |
| 00:50:12 | `A:Swing` |
| 00:50:19 | `B:HitLight:A` |
| 00:51:39 | `B:GetUp` |
| 00:51:47 | `Msg:SHOWCASE_UI:Result` |

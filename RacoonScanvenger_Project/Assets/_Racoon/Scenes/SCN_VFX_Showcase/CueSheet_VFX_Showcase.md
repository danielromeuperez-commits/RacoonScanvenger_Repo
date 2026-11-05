# Showcase VFX: tiempos para el montaje

Tiempo real de grabación (lo que verás en Premiere), mm:ss:frames a 60 fps. Duración total: 02:19:14.

## Bloques

| Efecto | Normal | Cámara lenta | Velocidad |
|---|---|---|---|
| Intro | 00:00:00 | - | - |
| Pasos | 00:03:03 | 00:06:18 | x0.3 |
| Carrera | 00:16:58 | 00:19:13 | x0.3 |
| Dash | 00:26:33 | 00:28:36 | x0.3 |
| Golpe al aire | 00:35:17 | 00:36:50 | x0.3 |
| Carga y golpe cargado | 00:41:50 | 00:45:05 | x0.4 |
| Golpe ligero | 00:53:06 | 00:54:57 | x0.3 |
| Golpe fuerte y mareo | 01:00:57 | 01:05:00 | x0.4 |
| Impacto A - Estallido | 01:15:01 | 01:16:40 | x0.3 |
| Impacto B - Estrella | 01:22:01 | 01:23:40 | x0.3 |
| Impacto C - Relampagos | 01:29:00 | 01:30:39 | x0.3 |
| Impacto cargado | 01:35:59 | 01:37:38 | x0.3 |
| Cansancio | 01:42:58 | 01:46:31 | x0.5 |
| Estelas por jugador | 01:53:33 | 01:56:36 | x0.3 |
| Flecha del evento | 02:06:36 | 02:09:39 | x0.5 |
| Outro | 02:15:41 | - | - |

## Cues

| Tiempo real | Tiempo del clip | Cue |
|---|---|---|
| 00:01:00 | 1.00 s | `A:Swing` |
| 00:01:36 | 1.60 s | `C:Swing` |
| 00:02:12 | 2.20 s | `B:Swing` |
| 00:06:18 | 6.30 s | `Slow:0.3` |
| 00:16:54 | 9.48 s | `Slow:1` |
| 00:17:13 | 9.80 s | `A:Wind:Run` |
| 00:18:55 | 11.50 s | `A:Wind:Off` |
| 00:19:13 | 11.80 s | `Slow:0.3` |
| 00:20:03 | 12.05 s | `A:Wind:Run` |
| 00:25:43 | 13.75 s | `A:Wind:Off` |
| 00:26:29 | 13.98 s | `Slow:1` |
| 00:27:03 | 14.55 s | `A:Dash` |
| 00:27:45 | 15.25 s | `A:Wind:Off` |
| 00:27:48 | 15.30 s | `A:Steps:On` |
| 00:28:36 | 16.10 s | `Slow:0.3` |
| 00:30:16 | 16.60 s | `A:Dash` |
| 00:32:36 | 17.30 s | `A:Wind:Off` |
| 00:32:46 | 17.35 s | `A:Steps:On` |
| 00:35:12 | 18.08 s | `Slow:1` |
| 00:35:41 | 18.55 s | `A:Swing` |
| 00:36:50 | 19.70 s | `Slow:0.3` |
| 00:38:10 | 20.10 s | `A:Swing` |
| 00:41:46 | 21.18 s | `Slow:1` |
| 00:42:08 | 21.55 s | `A:Charge:1.4` |
| 00:44:08 | 23.55 s | `A:ChargeEnd` |
| 00:44:08 | 23.55 s | `A:SwingCharged` |
| 00:45:05 | 24.50 s | `Slow:0.4` |
| 00:45:50 | 24.80 s | `A:Charge:1.4` |
| 00:50:50 | 26.80 s | `A:ChargeEnd` |
| 00:50:50 | 26.80 s | `A:SwingCharged` |
| 00:53:02 | 27.68 s | `Slow:1` |
| 00:53:24 | 28.05 s | `A:Swing` |
| 00:53:31 | 28.17 s | `B:HitLight:A` |
| 00:54:51 | 29.50 s | `B:GetUp` |
| 00:54:57 | 29.60 s | `Slow:0.3` |
| 00:55:57 | 29.90 s | `A:Swing` |
| 00:56:21 | 30.02 s | `B:HitLight:A` |
| 01:00:47 | 31.35 s | `B:GetUp` |
| 01:00:53 | 31.38 s | `Slow:1` |
| 01:01:15 | 31.75 s | `A:SwingCharged` |
| 01:01:22 | 31.87 s | `B:HitHeavy:A` |
| 01:04:54 | 35.40 s | `B:GetUp` |
| 01:05:00 | 35.50 s | `Slow:0.4` |
| 01:05:45 | 35.80 s | `A:SwingCharged` |
| 01:06:03 | 35.92 s | `B:HitHeavy:A` |
| 01:14:53 | 39.45 s | `B:GetUp` |
| 01:14:57 | 39.48 s | `Slow:1` |
| 01:15:22 | 39.90 s | `C:Test:Impact:TestBurst` |
| 01:16:40 | 41.20 s | `Slow:0.3` |
| 01:17:50 | 41.55 s | `C:Test:Impact:TestBurst` |
| 01:21:56 | 42.78 s | `Slow:1` |
| 01:22:22 | 43.20 s | `C:Test:Impact:TestStar` |
| 01:23:40 | 44.50 s | `Slow:0.3` |
| 01:24:50 | 44.85 s | `C:Test:Impact:TestStar` |
| 01:28:56 | 46.08 s | `Slow:1` |
| 01:29:21 | 46.50 s | `C:Test:Impact:TestBolts` |
| 01:30:39 | 47.80 s | `Slow:0.3` |
| 01:31:49 | 48.15 s | `C:Test:Impact:TestBolts` |
| 01:35:55 | 49.38 s | `Slow:1` |
| 01:36:20 | 49.80 s | `C:Test:Impact:TestCharged` |
| 01:37:38 | 51.10 s | `Slow:0.3` |
| 01:38:48 | 51.45 s | `C:Test:Impact:TestCharged` |
| 01:42:54 | 52.68 s | `Slow:1` |
| 01:43:01 | 52.80 s | `B:Tired:3.4` |
| 01:46:31 | 56.30 s | `Slow:0.5` |
| 01:46:37 | 56.35 s | `B:Tired:3.4` |
| 01:53:29 | 59.78 s | `Slow:1` |
| 01:53:48 | 60.10 s | `A:Wind:Run` |
| 01:53:48 | 60.10 s | `B:Wind:Run` |
| 01:53:48 | 60.10 s | `C:Wind:Run` |
| 01:55:36 | 61.90 s | `A:Wind:Off` |
| 01:55:36 | 61.90 s | `B:Wind:Off` |
| 01:55:36 | 61.90 s | `C:Wind:Off` |
| 01:56:36 | 62.90 s | `Slow:0.3` |
| 01:57:26 | 63.15 s | `A:Wind:Run` |
| 01:57:26 | 63.15 s | `B:Wind:Run` |
| 01:57:26 | 63.15 s | `C:Wind:Run` |
| 02:03:26 | 64.95 s | `A:Wind:Off` |
| 02:03:26 | 64.95 s | `B:Wind:Off` |
| 02:03:26 | 64.95 s | `C:Wind:Off` |
| 02:06:32 | 65.88 s | `Slow:1` |
| 02:06:54 | 66.25 s | `Arrow:Show` |
| 02:09:30 | 68.85 s | `Arrow:Hide` |
| 02:09:39 | 69.00 s | `Slow:0.5` |
| 02:10:15 | 69.30 s | `Arrow:Show` |
| 02:15:27 | 71.90 s | `Arrow:Hide` |
| 02:15:37 | 71.98 s | `Slow:1` |
| 02:16:41 | 73.05 s | `A:Swing` |
| 02:16:47 | 73.15 s | `B:HitLight:A` |
| 02:17:35 | 73.95 s | `C:Test:Impact:TestStar` |
| 02:19:05 | 75.45 s | `B:GetUp` |

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winsock2.h>
#include <ws2tcpip.h>

#pragma comment(lib, "ws2_32.lib")
#endif

#include "m_ai.h"
// #include <curl/curl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "d_main.h"
#include "doomstat.h"
#include "g_game.h"
#include "i_system.h"
#include "i_threads.h"
#include "i_time.h"
#include "keys.h"
#include "m_random.h" // For P_RandomChance
#include "netcode/d_netcmd.h"
#include "p_local.h"
#include "p_mobj.h"
#include "r_main.h"
#include "s_sound.h"
#include "sounds.h" // For sfx_1up
#include "tables.h"

extern INT32 displayplayer;
extern player_t players[MAXPLAYERS];
extern INT16 gamemap;
extern INT16 gamemap;
extern tic_t leveltime;
extern thinker_t thlist[];

ai_board_t ai_board;
int ai_running = 0;
INT32 ai_listening = 0;

// AI Inputs (Global)
SINT8 ai_forwardmove = 0;
SINT8 ai_sidemove = 0;
INT16 ai_angleturn = 0;
UINT32 ai_buttons = 0;

// Menu/System commands
static INT32 ai_sys_enter = 0;
static INT32 ai_sys_up = 0;
static INT32 ai_sys_down = 0;
static INT32 ai_sys_left = 0;
static INT32 ai_sys_right = 0;
static INT32 ai_sys_escape = 0;

typedef enum {
  AI_STATE_IDLE,
  AI_STATE_THINKING,
  AI_STATE_RESPONDING
} ai_stream_state_t;

struct stream_context {
  ai_stream_state_t state;
  char full_buffer[8192];
  char line_remainder[2048]; // Para manejar lineas partidas por curl
  size_t remainder_len;
  size_t thinking_tokens;
};

// Simple UTF-8 to Extended ASCII (CP1252) converter
static void ConvertUTF8ToCP1252(char *out, const char *in, size_t out_size) {
  size_t i = 0, j = 0;
  while (in[i] && j < out_size - 1) {
    unsigned char c = (unsigned char)in[i];
    if (c < 0x80) {
      out[j++] = in[i++];
    } else if ((c & 0xE0) == 0xC0 && (unsigned char)in[i + 1]) {
      // 2-byte sequence (Spanish accents are here)
      int high = (c & 0x1F) << 6;
      int low = (unsigned char)in[i + 1] & 0x3F;
      int code = high | low;
      if (code <= 0xFF) {
        out[j++] = (char)code;
      } else {
        out[j++] = '?';
      }
      i += 2;
    } else {
      // ignore 3-4 byte sequences or invalid
      out[j++] = '?';
      i++;
      // Skip other multi-byte chars if possible
      if ((c & 0xF0) == 0xE0 && in[i] && in[i + 1])
        i += 2;
      else if ((c & 0xF8) == 0xF0 && in[i] && in[i + 1] && in[i + 2])
        i += 3;
    }
  }
  out[j] = '\0';
}

// Extrae el contenido del delta en streaming (OpenAI format)
static void HandleStreamingChunk(const char *chunk,
                                 struct stream_context *ctx) {
  const char *data_prefix = "data: ";
  if (strncmp(chunk, data_prefix, 6) != 0) {
    // if (chunk[0] != '\0') I_OutputMsg("AI_RAW_CHUNK: %s\n", chunk);
    return;
  }

  const char *json_body = chunk + 6;
  // I_OutputMsg("AI_JSON: %s\n", json_body);
  if (strstr(json_body, "[DONE]"))
    return;

  const char *key = "\"content\":\"";
  char *start = strstr(json_body, key);
  if (!start) {
    key = "\"content\": \"";
    start = strstr(json_body, key);
  }
  if (!start) {
    key = "\"reasoning_content\":\"";
    start = strstr(json_body, key);
  }

  if (start) {
    start += strlen(key);
    char delta[2048];
    size_t i = 0;
    while (start[i] && i < sizeof(delta) - 1) {
      if (start[i] == '\"') {
        if (i == 0 || start[i - 1] != '\\')
          break;
      }
      delta[i] = start[i];
      i++;
    }
    delta[i] = '\0';
    // I_OutputMsg("AI_Delta: [%s]\n", delta);

    // Manejar etiquetas de pensamiento <think>
    if (strstr(delta, "<think>")) {
      ctx->state = AI_STATE_THINKING;
      // I_OutputMsg("AI_State: THINKING\n");
      return;
    }
    if (strstr(delta, "</think>")) {
      ctx->state = AI_STATE_RESPONDING;
      // I_OutputMsg("AI_State: RESPONDING\n");
      return;
    }

    if (ctx->state != AI_STATE_THINKING) {
      strncat(ctx->full_buffer, delta,
              sizeof(ctx->full_buffer) - strlen(ctx->full_buffer) - 1);

      I_lock_mutex(&ai_board.lock);
      strncpy(ai_board.buffer, ctx->full_buffer, sizeof(ai_board.buffer) - 1);
      ai_board.buffer[sizeof(ai_board.buffer) - 1] = '\0';
      I_unlock_mutex(ai_board.lock);
    } else {
      ctx->thinking_tokens++;
      I_lock_mutex(&ai_board.lock);
      // snprintf(ai_board.buffer, sizeof(ai_board.buffer),
      //          "(Pensando... %zu tokens)", ctx->thinking_tokens);
      I_unlock_mutex(ai_board.lock);
    }
  }
}

static size_t streaming_writefunc(void *ptr, size_t size, size_t nmemb,
                                  struct stream_context *ctx) {
  size_t total = size * nmemb;
  // I_OutputMsg("AI_CHUNK: %zu bytes\n", total);

  // Unir el resto anterior con los nuevos datos
  size_t needed = ctx->remainder_len + total + 1;
  char *combined = malloc(needed);
  if (!combined)
    return total;

  memcpy(combined, ctx->line_remainder, ctx->remainder_len);
  memcpy(combined + ctx->remainder_len, ptr, total);
  combined[ctx->remainder_len + total] = '\0';

  char *current = combined;
  char *newline;

  // Procesar todas las lineas completas
  while ((newline = strchr(current, '\n')) != NULL) {
    *newline = '\0';
    if (strlen(current) > 0) {
      // I_OutputMsg("AI_LINE: %s\n", current);
      HandleStreamingChunk(current, ctx);
    }
    current = newline + 1;
  }

  // Guardar lo que quedo (linea incompleta)
  size_t left = strlen(current);
  if (left < sizeof(ctx->line_remainder) - 1) {
    strcpy(ctx->line_remainder, current);
    ctx->remainder_len = left;
  } else {
    ctx->remainder_len = 0; // Buffer overflow, descartar
  }

  free(combined);
  return total;
}

static void AI_Thread_Func(void *v) {
  (void)v;
  // CURL *curl;

  // curl = curl_easy_init();
  // if (!curl)
  //   return;

  while (ai_running && !I_thread_is_stopped()) {
    // ... (logic disabled)
    I_Sleep(500);
  }

  // curl_easy_cleanup(curl);
}

// Remote Control Interface using TCP
#include "d_main.h"

consvar_t cv_ai_controlled =
    CVAR_INIT("ai_controlled", "On", CV_SAVE, CV_OnOff, NULL);

typedef enum { CMD_TYPE_CONSOLE, CMD_TYPE_KEY_EVENT } cmd_type_t;

typedef struct cmd_node_s {
  cmd_type_t type;
  char *cmd;
  event_t ev;
  int wait_ticks;
  struct cmd_node_s *next;
} cmd_node_t;

static cmd_node_t *cmd_queue_head = NULL;
static cmd_node_t *cmd_queue_tail = NULL;
static I_mutex remote_cmd_lock;

// Telemetry Snapshot
typedef struct {
  int gamestate;
  int menuactive;
  int gamemap;
  int leveltime;
  int x, y, z;
  unsigned int angle;
  int rings;
  unsigned int score;
  int lives;
  int speed;
  int powers[4]; // shield, invinc, sneakers, super
  int eflags;
  int underwater;
  int blocked;
  int enemy_dist;
  unsigned int enemy_angle;
  int obj_dist;
  unsigned int obj_angle;
  int hints;
  int checkpoint;          // starpostnum - last checkpoint hit
  int timeshit;            // number of times hit
  char enemy_type[128];    // Type names of nearby enemies (comma separated)
  char mapname[64];        // Full map name (e.g. "Greenflower Zone Act 1")
  char objective_type[64]; // Type of objective
  char last_transcription[256]; // Last player voice transcription
  int listening;                // Is listening for voice
} tel_snapshot_t;

static tel_snapshot_t tel_snap;
static I_mutex tel_lock;

// --- AI Command Handlers ---

static void Command_AI_Forward_f(void) {
  if (COM_Argc() < 2)
    return;
  ai_forwardmove = (SINT8)atoi(COM_Argv(1));
}

static void Command_AI_Side_f(void) {
  if (COM_Argc() < 2)
    return;
  ai_sidemove = (SINT8)atoi(COM_Argv(1));
}

static void Command_AI_Turn_f(void) {
  if (COM_Argc() < 2)
    return;
  ai_angleturn = (INT16)atoi(COM_Argv(1));
}

static void Command_AI_Jump_f(void) {
  if (COM_Argc() < 2)
    return;
  if (atoi(COM_Argv(1)))
    ai_buttons |= BT_JUMP;
  else
    ai_buttons &= ~BT_JUMP;
}

static void Command_AI_Spin_f(void) {
  if (COM_Argc() < 2)
    return;
  if (atoi(COM_Argv(1)))
    ai_buttons |= BT_SPIN;
  else
    ai_buttons &= ~BT_SPIN;
}

// === AI CHEAT COMMANDS (Bypasses regular cheat protection for AI Fun) ===

static void Command_AddLives_f(void) {
  if (COM_Argc() < 2)
    return;
  int num = atoi(COM_Argv(1));
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    // CONS_Printf("AI_EXEC: Giving %d lives to player %d\n", num,
    // consoleplayer);
    P_GivePlayerLives(p, num);
    S_StartSound(p->mo, sfx_oneup);
  } else {
    CONS_Printf("AI_EXEC ERROR: No player mobj for lives\n");
  }
}

static void Command_SubLives_f(void) {
  if (COM_Argc() < 2)
    return;
  int num = atoi(COM_Argv(1));
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    /* CONS_Printf("AI_EXEC: Removing %d lives from player %d\n", num,
                consoleplayer); */
    if (p->lives > num && p->lives != INFLIVES)
      p->lives -= num;
    else if (p->lives != INFLIVES)
      p->lives = 1;
    S_StartSound(p->mo, sfx_mixup); // Play a "bad" sound
  } else {
    CONS_Printf("AI_EXEC ERROR: No player mobj for sublives\n");
  }
}

static void Command_AddRings_f(void) {
  if (COM_Argc() < 2)
    return;
  int num = atoi(COM_Argv(1));
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    // CONS_Printf("AI_EXEC: Adding %d rings to player %d\n", num,
    // consoleplayer);
    p->rings += (INT16)num;
    if (p->rings < 0)
      p->rings = 0;
    S_StartSound(p->mo, sfx_itemup);
  } else {
    CONS_Printf("AI_EXEC ERROR: No player mobj for rings\n");
  }
}

static void Command_SetScale_f(void) {
  if (COM_Argc() < 2)
    return;
  float scale = (float)atof(COM_Argv(1));
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    // CONS_Printf("AI_EXEC: Multiplying scale by %.2f for player %d\n", scale,
    // consoleplayer);
    p->mo->destscale = FixedMul(p->mo->destscale, (fixed_t)(scale * FRACUNIT));
    S_StartSound(p->mo, sfx_mixup);
  }
}

static void Command_ResetScale_f(void) {
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    // CONS_Printf("AI_EXEC: Resetting scale for player %d\n", consoleplayer);
    p->mo->destscale = FRACUNIT;
    S_StartSound(p->mo, sfx_mixup);
  }
}

static void Command_GodMode_f(void) {
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    p->pflags ^= PF_GODMODE;
    // CONS_Printf("AI_EXEC: God Mode %s for player %d\n", (p->pflags &
    // PF_GODMODE) ? "ON" : "OFF", consoleplayer);
    S_StartSound(p->mo, sfx_oneup);
  }
}

static void Command_Teleport_f(void) {
  if (COM_Argc() < 2)
    return;
  int height = atoi(COM_Argv(1));
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    // CONS_Printf("AI_EXEC: Teleporting player %d to Z=%d\n", consoleplayer,
    // height);
    P_SetOrigin(p->mo, p->mo->x, p->mo->y, height * FRACUNIT);
  }
}

static void Command_ForceJump_f(void) {
  player_t *p = &players[consoleplayer];
  if (p->mo) {
    // CONS_Printf("AI_EXEC: Forcing jump for player %d\n", consoleplayer);
    p->mo->momz = 10 * FRACUNIT;
  }
}

static void Command_SetGravity_f(void) {
  if (COM_Argc() < 2)
    return;
  // Robustly find and set gravity
  consvar_t *grav_cv = CV_FindVar("gravity");
  if (grav_cv) {
    CV_Set(grav_cv, COM_Argv(1));
  } else {
    // Fallback if gravity var isn't found by name
    CV_Set(&cv_gravity, COM_Argv(1));
  }
}

// New Actions for Dashboard
static void Command_HurtMe_f(void) {
  player_t *p = &players[consoleplayer];
  if (p->mo && p->mo->health > 0) {
    P_DamageMobj(p->mo, NULL, NULL, 1, 0);
  }
}

static void Command_KillMe_f(void) {
  player_t *p = &players[consoleplayer];
  if (p->mo && p->mo->health > 0) {
    P_KillMobj(p->mo, NULL, NULL, 0);
  }
}

// Play sound by name (for dashboard/external control)
static void Command_PlaySound_f(void) {
  if (COM_Argc() < 2) {
    CONS_Printf("playsound <soundname>: Play a sound effect\n");
    CONS_Printf("Examples: playsound sfx_itemup, playsound sfx_shldls, "
                "playsound sfx_lose\n");
    return;
  }

  const char *soundname = COM_Argv(1);
  player_t *p = &players[consoleplayer];

  // Search for the sound by name
  sfxenum_t sfx_id = sfx_None;
  for (INT32 i = sfx_None + 1; i < NUMSFX; i++) {
    if (S_sfx[i].name && strcasecmp(S_sfx[i].name, soundname) == 0) {
      sfx_id = i;
      break;
    }
  }

  if (sfx_id == sfx_None) {
    CONS_Printf("Unknown sound: %s\n", soundname);
    return;
  }

  // Play from player position or globally
  if (p->mo) {
    S_StartSound(p->mo, sfx_id);
  } else {
    S_StartSound(NULL, sfx_id);
  }
}

static void Command_MultiplyEnemies_f(void) {
  player_t *p = &players[consoleplayer];
  if (!p->mo)
    return;

  mobj_t *mo = p->mo;
  thinker_t *th;
  mobj_t *mobj;
  int count = 0;

  // Scan for enemies
  for (th = thlist[THINK_MOBJ].next; th != &thlist[THINK_MOBJ]; th = th->next) {
    if (th->function == (actionf_p1)P_RemoveThinker)
      continue;

    mobj = (mobj_t *)th;

    if (mobj == p->mo)
      continue;

    if (mobj->health <= 0)
      continue;

    // Check if it is an enemy
    if ((mobj->flags & (MF_ENEMY | MF_BOSS))) {
      fixed_t dist = P_AproxDistance(mobj->x - mo->x, mobj->y - mo->y);
      if (dist < 1500 * FRACUNIT) { // Nearby enemies only
        // Spawn a copy
        mobj_t *copy = P_SpawnMobjFromMobj(mobj, 0, 0, 0, mobj->type);
        if (copy) {
          // Offset slightly so they don't get stuck instantly
          if (P_RandomChance(FRACUNIT / 2))
            copy->x += 50 * FRACUNIT;
          else
            copy->x -= 50 * FRACUNIT;

          if (P_RandomChance(FRACUNIT / 2))
            copy->y += 50 * FRACUNIT;
          else
            copy->y -= 50 * FRACUNIT;

          S_StartSound(copy, sfx_pop); // Sound effect for spawning
          count++;
          if (count >= 10)
            break; // Limit to 10 copies per click
        }
      }
    }
  }

  if (count > 0) {
    CONS_Printf("AI_EXEC: Multiplied %d enemies!\n", count);
  }
}

// Find highest stacked block at (x, y)
static fixed_t AI_GetStackZ(fixed_t x, fixed_t y, fixed_t startz) {
  thinker_t *th;
  mobj_t *mobj;
  fixed_t z = startz;
  for (th = thlist[THINK_MOBJ].next; th != &thlist[THINK_MOBJ]; th = th->next) {
    if (th->function == (actionf_p1)P_RemoveThinker)
      continue;
    mobj = (mobj_t *)th;
    // Stack on solid blocks we create
    if (mobj->type == MT_GARGOYLE || mobj->type == MT_BUSH ||
        mobj->type == MT_BLUECRYSTAL) {
      if (P_AproxDistance(mobj->x - x, mobj->y - y) < 48 * FRACUNIT) {
        if (mobj->z + mobj->height > z)
          z = mobj->z + mobj->height;
      }
    }
  }
  return z;
}

static void Command_SpawnBlock_f(void) {
  player_t *p = &players[consoleplayer];
  if (!p->mo)
    return;

  fixed_t x = p->mo->x + FixedMul(96 * FRACUNIT,
                                  FINECOSINE(p->mo->angle >> ANGLETOFINESHIFT));
  fixed_t y = p->mo->y + FixedMul(96 * FRACUNIT,
                                  FINESINE(p->mo->angle >> ANGLETOFINESHIFT));
  fixed_t z = AI_GetStackZ(x, y, p->mo->z);

  mobj_t *block = P_SpawnMobj(x, y, z, MT_GARGOYLE);
  if (block) {
    block->flags |= (MF_SOLID | MF_SHOOTABLE | MF_NOGRAVITY);
    CONS_Printf("AI_EXEC: Gargola creada (Z=%d)\n", z >> FRACBITS);
    S_StartSound(block, sfx_pop);
  }
}

static void Command_SpawnBlockMC_f(void) {
  player_t *p = &players[consoleplayer];
  if (!p->mo)
    return;

  fixed_t x = p->mo->x + P_ReturnThrustX(p->mo, p->mo->angle, 128 * FRACUNIT);
  fixed_t y = p->mo->y + P_ReturnThrustY(p->mo, p->mo->angle, 128 * FRACUNIT);
  fixed_t z = AI_GetStackZ(x, y, p->mo->z);

  mobj_t *block = P_SpawnMobj(x, y, z, MT_MCBLOCK);
  if (block) {
    CONS_Printf("AI_EXEC: Bloque Minecraft creado (Z=%d)\n", z >> FRACBITS);
    S_StartSound(block, sfx_pop);
  }
}

static void Command_SpawnTree_f(void) {
  player_t *p = &players[consoleplayer];
  if (!p->mo)
    return;

  fixed_t x = p->mo->x + FixedMul(96 * FRACUNIT,
                                  FINECOSINE(p->mo->angle >> ANGLETOFINESHIFT));
  fixed_t y = p->mo->y + FixedMul(96 * FRACUNIT,
                                  FINESINE(p->mo->angle >> ANGLETOFINESHIFT));
  fixed_t z = p->mo->z; // Trees don't stack for now

  mobj_t *block = P_SpawnMobj(x, y, z, MT_BUSH);
  if (block) {
    // Purely decorative (Arbolito)
    block->flags &= ~(MF_SOLID | MF_PUSHABLE | MF_SHOOTABLE | MF_SPECIAL);
    block->flags |= MF_NOGRAVITY;
    CONS_Printf("AI_EXEC: Arbolito decorativo creado\n");
    S_StartSound(block, sfx_pop);
  }
}

static void Command_SpawnAny_f(void) {
  if (COM_Argc() < 2)
    return;

  mobjtype_t type = (mobjtype_t)atoi(COM_Argv(1));
  if (type <= MT_NULL || type >= NUMMOBJTYPES) {
    CONS_Printf("AI_EXEC ERROR: Tipo de objeto invalido: %d\n", type);
    return;
  }

  player_t *p = &players[consoleplayer];
  if (!p->mo)
    return;

  fixed_t x = p->mo->x + FixedMul(96 * FRACUNIT,
                                  FINECOSINE(p->mo->angle >> ANGLETOFINESHIFT));
  fixed_t y = p->mo->y + FixedMul(96 * FRACUNIT,
                                  FINESINE(p->mo->angle >> ANGLETOFINESHIFT));
  fixed_t z = AI_GetStackZ(x, y, p->mo->z);

  mobj_t *mobj = P_SpawnMobj(x, y, z, type);
  if (mobj) {
    // Basic solid/gravity settings for spawned items
    mobj->flags |= (MF_NOGRAVITY);
    // If it was supposed to be scenery, make it a bit more interactive
    if (mobj->flags & MF_SCENERY) {
      mobj->flags |= MF_SOLID;
    }
    CONS_Printf("AI_EXEC: Objeto [%d] creado en Z=%d\n", type, z >> FRACBITS);
    S_StartSound(mobj, sfx_pop);
  }
}

// Specialized Spawns for Quiz Rewards
static void AI_SpawnAhead(mobjtype_t type, boolean gravity) {
  player_t *p = &players[consoleplayer];
  if (!p->mo)
    return;

  // Calculate position ahead of Sonic
  fixed_t x = p->mo->x + P_ReturnThrustX(p->mo, p->mo->angle, 128 * FRACUNIT);
  fixed_t y = p->mo->y + P_ReturnThrustY(p->mo, p->mo->angle, 128 * FRACUNIT);
  fixed_t z = p->mo->z + 64 * FRACUNIT; // Spawns slightly higher

  mobj_t *mobj = P_SpawnMobj(x, y, z, type);
  if (mobj) {
    if (!gravity)
      mobj->flags |= MF_NOGRAVITY;

    // Give it some "throw" momentum
    P_InstaThrust(mobj, p->mo->angle, 8 * FRACUNIT);
    mobj->momz = 4 * FRACUNIT;

    CONS_Printf("AI_EXEC: Recompensa Tails [%d] lanzada!\n", type);
    S_StartSound(mobj, sfx_itemup);
  }
}

static void Command_SpawnRing_f(void) { AI_SpawnAhead(MT_RING, false); }
static void Command_SpawnGargoyle_f(void) { AI_SpawnAhead(MT_GARGOYLE, true); }
static void Command_SpawnCoin_f(void) { AI_SpawnAhead(MT_COIN, false); }
static void Command_Spawn1Up_f(void) { AI_SpawnAhead(MT_1UP_BOX, true); }
static void Command_SpawnRingBox_f(void) { AI_SpawnAhead(MT_RING_BOX, true); }

// Menu Commands (Pulsed)
static void Command_AI_MenuEnter_f(void) { ai_sys_enter = 5; }
static void Command_AI_MenuUp_f(void) { ai_sys_up = 5; }
static void Command_AI_MenuDown_f(void) { ai_sys_down = 5; }
static void Command_AI_MenuLeft_f(void) { ai_sys_left = 5; }
static void Command_AI_MenuRight_f(void) { ai_sys_right = 5; }
static void Command_AI_MenuEscape_f(void) { ai_sys_escape = 5; }

// Get enemy type name for telemetry
const char *GetEnemyTypeName(mobjtype_t type) {
  switch (type) {
  case MT_BLUECRAWLA:
    return "Crawla_Azul";
  case MT_REDCRAWLA:
    return "Crawla_Rojo";
  case MT_GFZFISH:
    return "Pez";
  case MT_GOLDBUZZ:
    return "Buzz_Dorado";
  case MT_REDBUZZ:
    return "Buzz_Rojo";
  case MT_JETTBOMBER:
    return "Jetty-Syn_Bomber";
  case MT_JETTGUNNER:
    return "Jetty-Syn_Gunner";
  case MT_CRAWLACOMMANDER:
    return "Comandante_Crawla";
  case MT_DETON:
    return "Deton";
  case MT_SKIM:
    return "Skim";
  case MT_TURRET:
    return "Torreta_Industrial";
  case MT_POPUPTURRET:
    return "Torreta_Pop-up";
  case MT_SPINCUSHION:
    return "Spincushion";
  case MT_CRUSHSTACEAN:
    return "Crushstacean";
  case MT_BANPYURA:
    return "Banpyura";
  case MT_JETJAW:
    return "Jet_Jaw";
  case MT_SNAILER:
    return "Snailer";
  case MT_VULTURE:
    return "BASH";
  case MT_POINTY:
    return "Pointy";
  case MT_ROBOHOOD:
    return "Robo-Hood";
  case MT_FACESTABBER:
    return "Castlebot_Facestabber";
  case MT_EGGGUARD:
    return "Egg_Guard";
  case MT_GSNAPPER:
    return "Green_Snapper";
  case MT_MINUS:
    return "Minus";
  case MT_SPRINGSHELL:
    return "Spring_Shell";
  case MT_UNIDUS:
    return "Unidus";
  case MT_CANARIVORE:
    return "Canarivore";
  case MT_PYREFLY:
    return "Pyre_Fly";
  case MT_PTERABYTE:
    return "Pterabyte";
  case MT_DRAGONBOMBER:
    return "Dragonbomber";
  case MT_EGGMOBILE:
  case MT_EGGMOBILE2:
  case MT_EGGMOBILE3:
  case MT_EGGMOBILE4:
    return "Boss_Eggman";
  default:
    // If none of the specific ones, but it's an enemy or boss, return generic
    return "Enemigo";
  }
}

// Get objective type name for telemetry
const char *GetObjectiveTypeName(mobjtype_t type, int starpostnum) {
  switch (type) {
  case MT_SIGN:
    return "Final_del_Nivel";
  case MT_STARPOST: {
    static char buf[32];
    snprintf(buf, sizeof(buf), "Checkpoint_%d", starpostnum + 1);
    return buf;
  }
  case MT_TOKEN:
    return "Token_Special_Stage";
  case MT_EMBLEM:
    return "Emblema";
  case MT_EMERALD1:
  case MT_EMERALD2:
  case MT_EMERALD3:
  case MT_EMERALD4:
  case MT_EMERALD5:
  case MT_EMERALD6:
  case MT_EMERALD7:
    return "Esmeralda_del_Caos";
  default:
    return "Objetivo";
  }
}

// Scan for closest enemy and objective
void AI_ScanEntities(mobj_t *source, mobj_t **enemy, fixed_t *e_dist,
                     angle_t *e_ang, mobj_t **goal, fixed_t *g_dist,
                     angle_t *g_ang) {
  mobj_t *closest_enemy = NULL;
  fixed_t closest_e_dist = INT_MAX;
  mobj_t *closest_goal = NULL;
  fixed_t closest_g_dist = INT_MAX; // Much larger range for objectives
  thinker_t *th;

  if (!source)
    return;

  // Clear names first
  tel_snap.enemy_type[0] = '\0';
  strcpy(tel_snap.objective_type, "Objetivo");

  int enemy_count = 0;
  char enemy_list[128];
  enemy_list[0] = '\0';

  for (th = thlist[THINK_MOBJ].next; th != &thlist[THINK_MOBJ]; th = th->next) {
    mobj_t *mobj = (mobj_t *)th;

    // Skip if same as source or dead
    if (!mobj || mobj == source || mobj->health <= 0)
      continue;

    fixed_t d = P_AproxDistance(mobj->x - source->x, mobj->y - source->y);

    // Enemy Check
    if ((mobj->flags & (MF_ENEMY | MF_BOSS)) && !(mobj->flags & MF_PUSHABLE)) {
      if (d < 2048 * FRACUNIT) { // Only list enemies within a reasonable range
        const char *ename = GetEnemyTypeName(mobj->type);
        if (enemy_count < 5 && !strstr(enemy_list, ename)) {
          if (enemy_count > 0)
            strcat(enemy_list, ",");
          strcat(enemy_list, ename);
          enemy_count++;
        }
      }

      if (d < closest_e_dist) {
        closest_e_dist = d;
        closest_enemy = mobj;
      }
    }
    // Objective Check (Checkpoint, Sign, Token, Emblem)
    else if (mobj->type == MT_SIGN || mobj->type == MT_STARPOST ||
             mobj->type == MT_TOKEN || mobj->type == MT_EMBLEM ||
             (mobj->type >= MT_EMERALD1 && mobj->type <= MT_EMERALD7)) {

      // For starposts, only consider if it's NOT the last one we hit
      if (mobj->type == MT_STARPOST) {
        player_t *p = source->player;
        if (p && mobj->health <= p->starpostnum)
          continue;
      }

      if (d < closest_g_dist) {
        closest_g_dist = d;
        closest_goal = mobj;
      }
    }
  }

  // Set Enemy Name
  if (enemy_count > 0) {
    strncpy(tel_snap.enemy_type, enemy_list, sizeof(tel_snap.enemy_type) - 1);
  } else {
    strcpy(tel_snap.enemy_type, "Ninguno");
  }

  // Set Objective Name/Type
  if (closest_goal) {
    strncpy(tel_snap.objective_type,
            GetObjectiveTypeName(closest_goal->type, closest_goal->health - 1),
            sizeof(tel_snap.objective_type) - 1);
  }

  *enemy = closest_enemy;
  *e_dist = (closest_enemy) ? closest_e_dist : -1;
  if (closest_enemy)
    *e_ang = R_PointToAngle2(source->x, source->y, closest_enemy->x,
                             closest_enemy->y);
  else
    *e_ang = 0;

  *goal = closest_goal;
  *g_dist = (closest_goal) ? closest_g_dist : -1;
  if (closest_goal)
    *g_ang =
        R_PointToAngle2(source->x, source->y, closest_goal->x, closest_goal->y);
  else
    *g_ang = 0;
}

void AI_RemoteControl_Tick(void) {
  static int listen_debug_tic = 0;
  if (++listen_debug_tic >= 35) {
    listen_debug_tic = 0;
    //     if (ai_listening) {
    //       // CONS_Printf("SERVER STATUS: ai_listening=1\n");
    //     }
  }

  // Process Pulsed Menu Inputs
  if (ai_sys_enter > 0) {
    event_t ev;
    ev.type = (ai_sys_enter == 5) ? ev_keydown : ev_keyup;
    ev.key = KEY_ENTER;
    if (ai_sys_enter == 5 || ai_sys_enter == 1)
      D_PostEvent(&ev);
    ai_sys_enter--;
  }
  if (ai_sys_up > 0) {
    event_t ev;
    ev.type = (ai_sys_up == 5) ? ev_keydown : ev_keyup;
    ev.key = KEY_UPARROW;
    if (ai_sys_up == 5 || ai_sys_up == 1)
      D_PostEvent(&ev);
    ai_sys_up--;
  }
  if (ai_sys_down > 0) {
    event_t ev;
    ev.type = (ai_sys_down == 5) ? ev_keydown : ev_keyup;
    ev.key = KEY_DOWNARROW;
    if (ai_sys_down == 5 || ai_sys_down == 1)
      D_PostEvent(&ev);
    ai_sys_down--;
  }
  // ... rest of menu pulsers could follow, but Enter/Up/Down are key for now
  // ...

  // Update Telemetry Snapshot
  I_lock_mutex(&tel_lock);
  tel_snap.gamestate = gamestate;
  tel_snap.menuactive = menuactive;
  tel_snap.gamemap = (int)gamemap;
  tel_snap.leveltime = (int)leveltime;
  tel_snap.listening = ai_listening;

  // Capture Map Name
  if (gamemap > 0 && mapheaderinfo[gamemap - 1]) {
    char map_buf[64];
    if (mapheaderinfo[gamemap - 1]->actnum > 0)
      snprintf(map_buf, sizeof(map_buf), "%s_Act_%d",
               mapheaderinfo[gamemap - 1]->lvlttl,
               mapheaderinfo[gamemap - 1]->actnum);
    else
      snprintf(map_buf, sizeof(map_buf), "%s",
               mapheaderinfo[gamemap - 1]->lvlttl);

    // Replace spaces with underscores for easy parsing
    for (int i = 0; map_buf[i]; i++)
      if (map_buf[i] == ' ')
        map_buf[i] = '_';
    strncpy(tel_snap.mapname, map_buf, sizeof(tel_snap.mapname) - 1);
  } else {
    strcpy(tel_snap.mapname, "Menu");
  }

  // Always use consoleplayer for telemetry in AI sandbox to avoid
  // inconsistencies
  int target_player = consoleplayer;
  if (target_player >= 0 && target_player < MAXPLAYERS &&
      playeringame[target_player]) {
    player_t *p = &players[target_player];
    if (p) {
      if (p->mo) {
        tel_snap.x = p->mo->x;
        tel_snap.y = p->mo->y;
        tel_snap.z = p->mo->z;
        tel_snap.angle = p->mo->angle;
        tel_snap.eflags = (int)p->mo->eflags;

        static int tele_debug_tic = 0;
        if (++tele_debug_tic >= 35) {
          tele_debug_tic = 0;
          // CONS_Printf("AI_TELE_UPDATE: Z=%d RINGS=%d\n", tel_snap.z,
          // p->rings);
        }
      } else {
        tel_snap.x = 0;
        tel_snap.y = 0;
        tel_snap.z = 0;
        tel_snap.angle = 0;
        tel_snap.eflags = 0;
      }
      tel_snap.rings = p->rings;
      tel_snap.score = p->score;
      tel_snap.lives = p->lives;
      tel_snap.speed = (int)(p->speed / FRACUNIT);
      tel_snap.powers[0] = p->powers[pw_shield];
      tel_snap.powers[1] = (p->powers[pw_invulnerability] > 0);
      tel_snap.powers[2] = (p->powers[pw_sneakers] > 0);
      tel_snap.powers[3] = (p->powers[pw_super] > 0);
      tel_snap.underwater = (int)p->powers[pw_underwater];
      tel_snap.blocked = (int)p->blocked;
      tel_snap.checkpoint = (int)p->starpostnum;
      tel_snap.timeshit = (int)p->timeshit;

      // Sample entities in main thread (Safe)
      mobj_t *enemy = NULL;
      mobj_t *goal = NULL;
      fixed_t e_dist = 0, g_dist = 0;
      angle_t e_ang = 0, g_ang = 0;

      AI_ScanEntities(p->mo, &enemy, &e_dist, &e_ang, &goal, &g_dist, &g_ang);

      tel_snap.enemy_dist = (int)(e_dist >> FRACBITS);
      tel_snap.enemy_angle = (unsigned int)(e_ang >> ANGLETOFINESHIFT);
      tel_snap.obj_dist = (goal) ? (int)(g_dist >> FRACBITS) : -1;
      tel_snap.obj_angle = (unsigned int)(g_ang >> ANGLETOFINESHIFT);

      // Debug: Log enemy detection every 35 tics (1 second)
      static int debug_counter = 0;
      if (++debug_counter >= 35) {
        debug_counter = 0;
        //         if (enemy) {
        //           // I_OutputMsg("AI_DEBUG: Enemy detected! Type=%d, Dist=%d,
        //           Health=%d\n",
        //           //             enemy->type, tel_snap.enemy_dist,
        //           enemy->health);
        //         }
      }

      // Situation HINTS: bitfield
      // 1: Stuck, 2: Feet in water (TOUCHWATER), 4: Drowning, 8: Enemy, 16:
      // Fully submerged (UNDERWATER), 32: Flying with Tails
      int hints = 0;
      if (p->blocked || (ai_forwardmove != 0 && p->speed < 5 * FRACUNIT))
        hints |= 1; // Stuck

      // Water detection - differentiate between feet in water and fully
      // submerged
      if (p->mo) {
        if (p->mo->eflags & MFE_TOUCHWATER)
          hints |= 2; // Feet in water
        if (p->mo->eflags & MFE_UNDERWATER)
          hints |= 16; // Fully submerged
      }

      // Drowning detection
      if (p->powers[pw_underwater] > 0 &&
          p->powers[pw_underwater] < 11 * TICRATE)
        hints |= 4; // Drowning (running out of air)

      // Enemy proximity
      if (enemy && e_dist != -1 && e_dist < 512 * FRACUNIT)
        hints |= 8; // Enemy nearby

      // Tails carry detection
      if (p->powers[pw_carry] == CR_PLAYER)
        hints |= 32; // Flying with Tails

      tel_snap.hints = hints;
    }
  }
  I_unlock_mutex(tel_lock);

  I_lock_mutex(&remote_cmd_lock);
  cmd_node_t **curr = &cmd_queue_head;
  while (*curr) {
    cmd_node_t *node = *curr;
    if (node->wait_ticks > 0) {
      node->wait_ticks--;
      curr = &node->next;
      continue;
    }

    // Process node
    if (node->type == CMD_TYPE_CONSOLE) {
      // CONS_Printf("AI_EXEC: Running console command: %s\n", node->cmd);
      COM_BufAddText(node->cmd);
      COM_BufAddText("\n");
      free(node->cmd);
    } else if (node->type == CMD_TYPE_KEY_EVENT) {
      I_OutputMsg("AI_EXEC: Posting key event type %d key %d\n", node->ev.type,
                  node->ev.key);
      D_PostEvent(&node->ev);
    }

    // Remove node
    *curr = node->next;
    if (cmd_queue_tail == node) {
      // Re-find tail
      if (cmd_queue_head == NULL) {
        cmd_queue_tail = NULL;
      } else {
        cmd_node_t *t = cmd_queue_head;
        while (t->next)
          t = t->next;
        cmd_queue_tail = t;
      }
    }
    free(node);
    // Note: curr is now pointing to the next node, so we don't advance it
  }
  I_unlock_mutex(remote_cmd_lock);
}

static void RemoteControlThread(void *v) {
  (void)v;
#ifdef _WIN32
  WSADATA wsaData;
  if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
    I_OutputMsg("AI_REMOTE: WSAStartup failed\n");
    return;
  }

  SOCKET listen_sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
  if (listen_sock == INVALID_SOCKET) {
    I_OutputMsg("AI_REMOTE: Failed to create socket\n");
    WSACleanup();
    return;
  }

  int opt = 1;
  setsockopt(listen_sock, SOL_SOCKET, SO_REUSEADDR, (char *)&opt, sizeof(opt));

  struct sockaddr_in server;
  server.sin_family = AF_INET;
  server.sin_addr.s_addr = INADDR_ANY;
  server.sin_port = htons(1235);

  if (bind(listen_sock, (struct sockaddr *)&server, sizeof(server)) ==
      SOCKET_ERROR) {
    I_OutputMsg("AI_REMOTE: Error binding TCP 1235 (Error: %d)\n",
                WSAGetLastError());
    closesocket(listen_sock);
    WSACleanup();
    return;
  }

  if (listen(listen_sock, SOMAXCONN) == SOCKET_ERROR) {
    I_OutputMsg("AI_REMOTE: Listen failed\n");
    closesocket(listen_sock);
    WSACleanup();
    return;
  }

  // I_OutputMsg("AI_REMOTE: Escuchando comandos en TCP 1235 (Reliable)...\n");

  while (ai_running && !I_thread_is_stopped()) {
    struct sockaddr_in client_addr;
    int client_addr_len = sizeof(client_addr);
    SOCKET new_client_sock =
        accept(listen_sock, (struct sockaddr *)&client_addr, &client_addr_len);

    if (new_client_sock != INVALID_SOCKET) {
      char buffer[1024];
      int bytes_received = recv(new_client_sock, buffer, sizeof(buffer) - 1, 0);
      if (bytes_received > 0) {
        buffer[bytes_received] = '\0';

        if (strncmp(buffer, "TELEMETRY", 9) == 0) {
          char telemetry[1024];
          I_lock_mutex(&tel_lock);
          if (tel_snap.gamestate == GS_LEVEL) {
            snprintf(
                telemetry, sizeof(telemetry),
                "V 1.0.2 STATE %d MENU %d NIVEL %d MAPNAME %s TIME %d X %d Y "
                "%d Z %d "
                "ANGLE %u "
                "RINGS %d SCORE %u LIVES %d SPEED %d SHIELD %d INVUL %d "
                "SNEAKERS %d SUPER %d ENEMY_TYPE %s E_DIST %d E_ANG %u O_TYPE "
                "%s O_DIST %d O_ANG "
                "%u WATER %d DROWN %d HINT %d CHECKPOINT %d TIMESHIT %d "
                "LISTENING %d TRANS "
                "%s\n",
                tel_snap.gamestate, tel_snap.menuactive, tel_snap.gamemap,
                tel_snap.mapname, tel_snap.leveltime, tel_snap.x, tel_snap.y,
                tel_snap.z, tel_snap.angle, tel_snap.rings, tel_snap.score,
                tel_snap.lives, tel_snap.speed, tel_snap.powers[0],
                tel_snap.powers[1], tel_snap.powers[2], tel_snap.powers[3],
                tel_snap.enemy_type, tel_snap.enemy_dist, tel_snap.enemy_angle,
                tel_snap.objective_type, tel_snap.obj_dist, tel_snap.obj_angle,
                (tel_snap.eflags & MFE_UNDERWATER) ? 1 : 0, tel_snap.underwater,
                tel_snap.hints, tel_snap.checkpoint, tel_snap.timeshit,
                tel_snap.listening,
                (tel_snap.last_transcription[0] ? tel_snap.last_transcription
                                                : "Ninguna"));
          } else {
            snprintf(telemetry, sizeof(telemetry),
                     "STATE %d MENU %d NIVEL %d MAPNAME %s TIME %d X %d Y %d Z "
                     "%d ANGLE 0 "
                     "RINGS %d SCORE %u LIVES %d SPEED %d SHIELD %d INVUL %d "
                     "SNEAKERS %d SUPER %d ENEMY_TYPE Ninguno E_DIST -1 E_ANG "
                     "0 O_TYPE Objetivo O_DIST -1 O_ANG 0 "
                     "WATER 0 DROWN 0 HINT 0 CHECKPOINT 0 TIMESHIT 0 LISTENING "
                     "%d TRANS "
                     "Ninguna\n",
                     tel_snap.gamestate, tel_snap.menuactive, tel_snap.gamemap,
                     tel_snap.mapname, tel_snap.leveltime, tel_snap.x,
                     tel_snap.y, tel_snap.z, tel_snap.rings, tel_snap.score,
                     tel_snap.lives, tel_snap.speed, tel_snap.powers[0],
                     tel_snap.powers[1], tel_snap.powers[2], tel_snap.powers[3],
                     tel_snap.listening);
          }
          I_unlock_mutex(tel_lock);
          send(new_client_sock, telemetry, (int)strlen(telemetry), 0);
        } else if (strncmp(buffer, "ai_listening ", 13) == 0) {
          ai_listening = atoi(buffer + 13);
          //           CONS_Printf("DEBUG: ai_listening command received: %d\n",
          //                       ai_listening);
        } else if (strncmp(buffer, "SAY_IA ", 7) == 0) {
          // Receive AI message from Dashboard
          char decoded[1024];
          const char *msg = buffer + 7;
          ConvertUTF8ToCP1252(decoded, msg, sizeof(decoded));

          I_lock_mutex(&ai_board.lock);
          strncpy(ai_board.buffer, decoded, sizeof(ai_board.buffer) - 1);
          ai_board.buffer[sizeof(ai_board.buffer) - 1] = '\0';

          // Strip newlines/carriage returns that break HUD display
          {
            char *p = ai_board.buffer;
            while (*p) {
              if (*p == '\r' || *p == '\n') {
                *p = '\0';
                break;
              }
              p++;
            }
          }

          //           CONS_Printf("AI_RECV: '%s'\n", ai_board.buffer);
          ai_board.message_time = I_GetTime(); // Update timestamp

          // If it's a user transcription, store it for telemetry
          if (strncmp(msg, "USER:", 5) == 0) {
            const char *trans = msg + 5;
            if (*trans == ' ')
              trans++;
            //             I_OutputMsg("AI_REMOTE: Received transcription:
            //             %s\n", trans);
            I_lock_mutex(&tel_lock);
            strncpy(tel_snap.last_transcription, trans,
                    sizeof(tel_snap.last_transcription) - 1);
            tel_snap
                .last_transcription[sizeof(tel_snap.last_transcription) - 1] =
                '\0';
            // Replace spaces with underscores for telemetry protocol if needed,
            // but let's see if the parser can handle it.
            // Actually, telemetry is space-separated, so we MUST replace spaces
            // or quote.
            for (int i = 0; tel_snap.last_transcription[i]; i++)
              if (tel_snap.last_transcription[i] == ' ')
                tel_snap.last_transcription[i] = '_';
            I_unlock_mutex(tel_lock);
          }

          I_unlock_mutex(ai_board.lock);
          send(new_client_sock, "OK", 2, 0);
        } else if (strncmp(buffer, "KEY ", 4) == 0) {
          char type[16], keyname[64];
          if (sscanf(buffer + 4, "%15s %63s", type, keyname) == 2) {
            event_t ev;
            memset(&ev, 0, sizeof(ev));

            if (strcmp(type, "down") == 0)
              ev.type = ev_keydown;
            else if (strcmp(type, "up") == 0)
              ev.type = ev_keyup;
            else if (strcmp(type, "press") == 0)
              ev.type = ev_keydown;

            if (strcmp(keyname, "enter") == 0)
              ev.key = KEY_ENTER;
            else if (strcmp(keyname, "escape") == 0)
              ev.key = KEY_ESCAPE;
            else if (strcmp(keyname, "w") == 0)
              ev.key = 'w';
            else if (strcmp(keyname, "a") == 0)
              ev.key = 'a';
            else if (strcmp(keyname, "s") == 0)
              ev.key = 's';
            else if (strcmp(keyname, "d") == 0)
              ev.key = 'd';
            else if (strcmp(keyname, "space") == 0)
              ev.key = KEY_SPACE;
            else if (strcmp(keyname, "up") == 0)
              ev.key = KEY_UPARROW;
            else if (strcmp(keyname, "down") == 0)
              ev.key = KEY_DOWNARROW;
            else if (strcmp(keyname, "left") == 0)
              ev.key = KEY_LEFTARROW;
            else if (strcmp(keyname, "right") == 0)
              ev.key = KEY_RIGHTARROW;

            if (ev.key != 0) {
              I_lock_mutex(&remote_cmd_lock);
              cmd_node_t *new_node = malloc(sizeof(cmd_node_t));
              if (new_node) {
                new_node->type = CMD_TYPE_KEY_EVENT;
                new_node->ev = ev;
                new_node->wait_ticks = 0;
                new_node->next = NULL;
                if (cmd_queue_tail)
                  cmd_queue_tail->next = new_node;
                else
                  cmd_queue_head = new_node;
                cmd_queue_tail = new_node;

                if (strcmp(type, "press") == 0) {
                  cmd_node_t *up_node = malloc(sizeof(cmd_node_t));
                  if (up_node) {
                    up_node->type = CMD_TYPE_KEY_EVENT;
                    up_node->ev = ev;
                    up_node->ev.type = ev_keyup;
                    up_node->wait_ticks = 5; // Pulse duration
                    up_node->next = NULL;
                    cmd_queue_tail->next = up_node;
                    cmd_queue_tail = up_node;
                  }
                }
              }
              I_unlock_mutex(remote_cmd_lock);
            }
          }
        } else {
          char *line = strtok(buffer, "\n\r");
          while (line) {
            // Log received command for debugging
            I_OutputMsg("AI_REMOTE: Received command: [%s]\n", line);
            I_lock_mutex(&remote_cmd_lock);
            cmd_node_t *new_node = malloc(sizeof(cmd_node_t));
            if (new_node) {
              new_node->type = CMD_TYPE_CONSOLE;
              new_node->cmd = _strdup(line);
              new_node->wait_ticks = 0;
              new_node->next = NULL;
              if (cmd_queue_tail)
                cmd_queue_tail->next = new_node;
              else
                cmd_queue_head = new_node;
              cmd_queue_tail = new_node;
            }
            I_unlock_mutex(remote_cmd_lock);
            line = strtok(NULL, "\n\r");
          }
        }
      }
      closesocket(new_client_sock);
    } else {
      I_Sleep(10);
    }
  }

  closesocket(listen_sock);
  WSACleanup();
#endif
}

static void Command_AI_Listening_f(void) {
  if (COM_Argc() < 2) {
    CONS_Printf("ai_listening <0/1>: set listening state\n");
    return;
  }
  ai_listening = atoi(COM_Argv(1));
  //   CONS_Printf("DEBUG: ai_listening = %d\n", ai_listening);
}

void AI_Init(void) {
  ai_board.active = 0;
  ai_board.has_pending = 0;
  ai_board.buffer[0] = '\0';
  ai_running = 1;
  // CONS_Printf("AI_SYSTEM: Version 2.2-HUD-FINAL-TEST Initialized\n");

  // Mutexes are lazy-initialized via I_lock_mutex

  // Register CVars and Commands early
  CV_RegisterVar(&cv_ai_controlled);
  COM_AddCommand("ai_forward", Command_AI_Forward_f, 0);
  COM_AddCommand("ai_side", Command_AI_Side_f, 0);
  COM_AddCommand("ai_turn", Command_AI_Turn_f, 0);
  COM_AddCommand("ai_jump", Command_AI_Jump_f, 0);
  COM_AddCommand("ai_spin", Command_AI_Spin_f, 0);
  COM_AddCommand("ai_menu_enter", Command_AI_MenuEnter_f, 0);
  COM_AddCommand("ai_menu_up", Command_AI_MenuUp_f, 0);
  COM_AddCommand("ai_menu_down", Command_AI_MenuDown_f, 0);
  COM_AddCommand("ai_menu_left", Command_AI_MenuLeft_f, 0);
  COM_AddCommand("ai_menu_right", Command_AI_MenuRight_f, 0);
  COM_AddCommand("ai_menu_escape", Command_AI_MenuEscape_f, 0);
  COM_AddCommand("ai_listening", Command_AI_Listening_f, 0);

  // AI Fun Commands
  COM_AddCommand("addlives", Command_AddLives_f, 0);
  COM_AddCommand("sublives", Command_SubLives_f, 0);
  COM_AddCommand("addrings", Command_AddRings_f, 0);
  COM_AddCommand("ai_scale", Command_SetScale_f, 0);
  COM_AddCommand("ai_reset_scale", Command_ResetScale_f, 0);
  COM_AddCommand("ai_gravity", Command_SetGravity_f, 0);
  COM_AddCommand("ai_teleport", Command_Teleport_f, 0);
  COM_AddCommand("ai_force_jump", Command_ForceJump_f, 0);
  COM_AddCommand("ai_god", Command_GodMode_f, 0);

  // New Dashboard Actions
  COM_AddCommand("ai_hurt", Command_HurtMe_f, 0);
  COM_AddCommand("ai_kill", Command_KillMe_f, 0);
  COM_AddCommand("ai_multiply", Command_MultiplyEnemies_f, 0);
  COM_AddCommand("ai_spawn_block", Command_SpawnBlock_f, 0);
  COM_AddCommand("ai_spawn_block_mc", Command_SpawnBlockMC_f, 0);
  COM_AddCommand("ai_spawn_tree", Command_SpawnTree_f, 0);
  COM_AddCommand("ai_spawn", Command_SpawnAny_f, 0);
  COM_AddCommand("ai_spawn_ring", Command_SpawnRing_f, 0);
  COM_AddCommand("ai_spawn_gargoyle", Command_SpawnGargoyle_f, 0);
  COM_AddCommand("ai_spawn_coin", Command_SpawnCoin_f, 0);
  COM_AddCommand("ai_spawn_1up", Command_Spawn1Up_f, 0);
  COM_AddCommand("ai_spawn_ringbox", Command_SpawnRingBox_f, 0);
  COM_AddCommand("playsound", Command_PlaySound_f, 0);

  I_spawn_thread("AI_Worker", AI_Thread_Func, NULL);
#ifdef _WIN32
  I_spawn_thread("AI_Remote", RemoteControlThread, NULL);

  // Auto-launch AI Command Dashboard
  STARTUPINFOA si;
  PROCESS_INFORMATION pi;
  ZeroMemory(&si, sizeof(si));
  si.cb = sizeof(si);
  ZeroMemory(&pi, sizeof(pi));

  char dashboard_path[MAX_PATH];
  GetModuleFileNameA(NULL, dashboard_path, MAX_PATH);
  char *last_slash = strrchr(dashboard_path, '\\');
  if (last_slash) {
    strcpy(last_slash + 1, "AICommandDashboard.exe");
    if (CreateProcessA(NULL, dashboard_path, NULL, NULL, FALSE, 0, NULL, NULL,
                       &si, &pi)) {
      // CONS_Printf("AI Command Dashboard launched successfully.\n");
      CloseHandle(pi.hProcess);
      CloseHandle(pi.hThread);
    }
  }
#endif
}

void AI_Request(const char *context_data) {
  I_lock_mutex(&ai_board.lock);
  strncpy(ai_board.pending_prompt, context_data,
          sizeof(ai_board.pending_prompt) - 1);
  ai_board.pending_prompt[sizeof(ai_board.pending_prompt) - 1] = '\0';
  ai_board.has_pending = 1;
  I_unlock_mutex(ai_board.lock);
}

#ifdef HAVE_BLUA
#include "lua_libs.h"
#include "lua_script.h"

int LUA_AIRequest(lua_State *L) {
  const char *prompt = luaL_checkstring(L, 1);
  AI_Request(prompt);
  return 0;
}

int LUA_AILog(lua_State *L) {
  const char *msg = luaL_checkstring(L, 1);
  I_OutputMsg("AI_LOG: %s\n", msg);
  return 0;
}
#endif

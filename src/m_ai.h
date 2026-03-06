#ifndef __M_AI__
#define __M_AI__

#include "i_threads.h"

#ifdef HAVE_BLUA
#include "lua_script.h"
#endif

typedef struct {
  char buffer[8192];
  char pending_prompt[4096];
  int has_pending;
  int active;
  I_mutex lock;
  tic_t message_time; // Timestamp when message was received
} ai_board_t;

#include "command.h"

extern ai_board_t ai_board;
extern consvar_t cv_ai_controlled;
extern INT32 ai_listening;

// AI Inputs
extern SINT8 ai_forwardmove;
extern SINT8 ai_sidemove;
extern INT16 ai_angleturn;
extern UINT32 ai_buttons;

void AI_Init(void);
void AI_Request(const char *context_data);
void AI_RemoteControl_Tick(void);

#ifdef HAVE_BLUA
int LUA_AIRequest(lua_State *L);
int LUA_AILog(lua_State *L);
#endif

#endif

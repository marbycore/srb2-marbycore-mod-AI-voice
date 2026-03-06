-- SRB2 Defensive AI Logic
print("AI_LOGIC: Iniciando carga defensiva...")

local last_rings = 0

addHook("ThinkFrame", function()
    if not playeringame or not consoleplayer or not players then return end
    if not playeringame[consoleplayer] then return end
    
    local p = players[consoleplayer]
    if not p then return end

    -- Reaccionar a perdida de rings
    if p.rings < last_rings and p.rings == 0 then
        AI_Request("¡ACABAS DE PERDER TODOS TUS RINGS! Eres un desastre.")
    end
    last_rings = p.rings
end)

addHook("MobjDeath", function(target, inflictor, source)
    if not target or not target.player then return end
    if target.player == players[consoleplayer] then
        AI_Request("Has muerto de la forma más patética posible. Bravo.")
    end
end)

-- Test de arranque inmediato
if AI_Request then
    AI_Request("SISTEMA DE IA INICIADO. Responde en ESPAÑOL: '¡Hola! Estoy listo para comentar tus fallos.'")
    print("AI_LOGIC: Enviando test de arranque...")
end


-- Logica de destruccion del Bloque Minecraft
-- 1. Hook de Movimiento (Impacto directo a velocidad)
addHook("MobjMoveCollide", function(mobj, thing)
    if thing and thing.valid and thing.type == MT_MCBLOCK then
        if mobj.player then
            local p = mobj.player
            -- Verifica Spin Dash, Salto o Rodar
            local can_break = (p.pflags & (PF_SPINNING | PF_ROLLING | PF_JUMPED))
            
            if can_break then
                P_KillMobj(thing, mobj, mobj)
                print("MCBLOCK DESTROYED (MoveCollide)!")
                return false
            end
        end
    end
end, MT_PLAYER)

-- 2. Hook Estatico (Si el jugador ya esta dentro o tocando)
addHook("MobjCollide", function(thing, mobj)
    if thing.type == MT_MCBLOCK and mobj.player then
        local p = mobj.player
        local can_break = (p.pflags & (PF_SPINNING | PF_ROLLING | PF_JUMPED))
        
        if can_break then
            P_KillMobj(thing, mobj, mobj)
            print("MCBLOCK DESTROYED (Collide)!")
            return false
        end
    end
    return true -- Solido por defecto
end, MT_MCBLOCK)

-- 3. Verificacion Continua (ThinkFrame) para casos extremos
addHook("ThinkFrame", function()
    for block in mobs.iterate(MT_MCBLOCK) do
        if block and block.valid then
            local p = players[consoleplayer]
            if p and p.mo and p.mo.valid then
                -- Chequeo de distancia simple (Radio + Radio)
                local dx = block.x - p.mo.x
                local dy = block.y - p.mo.y
                local dist = FixedHypot(dx, dy)
                
                if dist < (block.radius + p.mo.radius) then
                    -- Chequeo de altura
                    if p.mo.z < block.z + block.height and p.mo.z + p.mo.height > block.z then
                        local can_break = (p.pflags & (PF_SPINNING | PF_ROLLING | PF_JUMPED))
                        if can_break then
                            P_KillMobj(block, p.mo, p.mo)
                            print("MCBLOCK DESTROYED (ThinkFrame)!")
                        end
                    end
                end
            end
        end
    end
end)

-- 4. Composite Logic (Body + Top)
addHook("MobjSpawn", function(body)
    -- Spawn the "Top" part slightly above the body
    local top = P_SpawnMobj(body.x, body.y, body.z + body.height, MT_MCBLOCK_TOP)
    if top and top.valid then
        top.target = body -- Link Top to Body
        body.tracer = top -- Link Body to Top
        print("Composite Block Spawned!")
    end
end, MT_MCBLOCK)

-- Destruccion vinculada (Cuando el cuerpo muere, la tapa muere)
addHook("MobjDeath", function(body, inflictor, source)
    if body.type == MT_MCBLOCK and body.tracer and body.tracer.valid then
        P_RemoveMobj(body.tracer) -- Remove Top part
    end
end, MT_MCBLOCK)

print("AI_LOGIC: Carga completada satisfactoriamente (Composite Enabled).")

-- SRB2 Pretty Telemetry & Focus Control
print("TELEMETRY_V2: Iniciando...")

-- Desactivar pausa al perder el foco para facilitar debugging
if CV_FindVar then
    local pfocus = CV_FindVar("pauseonlostfocus")
    if pfocus then
        CV_Set(pfocus, "Off")
        print("TELEMETRY: Pausa al perder foco DESACTIVADA.")
    end
end

local GS_MAP = {
    [4] = "Cinemática", [5] = "Créditos", [6] = "Evaluación", [7] = "Fin del Juego",
    [8] = "Intro", [9] = "En el Menú", [10] = "Pantalla de Título"
}

local function FindObjective(p)
    local best_dist = 999999*FRACUNIT
    local target_mo = nil
    local obj_type = "Ninguno"
    local heading = "N/A"
    
    -- Function to verify if an object is the objective
    local function check_obj(mo)
        if not mo or not mo.valid then return end
        
        -- Busca Signo de Final (MT_SIGN = 501 aprox, pero usaremos constantes si existen)
        if mo.type == MT_SIGN then
             local dist = R_PointToDist2(p.mo.x, p.mo.y, mo.x, mo.y)
             if dist < best_dist then
                 best_dist = dist
                 target_mo = mo
                 obj_type = "FINAL DE NIVEL"
             end
        end
        
        -- Busca Starpost (MT_STARPOST = 502)
        if mo.type == MT_STARPOST then
             -- Logic: Find the NEXT starpost? For now, just nearest.
             -- Usually starposts have 'health' as their ID (0, 1, 2...)
             local next_post = (p.starpostnum or 0) + 1
             if mo.health == next_post then
                 local dist = R_PointToDist2(p.mo.x, p.mo.y, mo.x, mo.y)
                 if dist < best_dist then -- PRIORITIZE Starpost over sign if closer? Or logic?
                     best_dist = dist
                     target_mo = mo
                     obj_type = "CHECKPOINT #"..tostring(next_post)
                 end
             end
        end
    end
    
    -- Intenta iterar globalmente (si existe thinkers.iterate o similar)
    -- SRB2 Lua usually exposes 'mobj.iterate()' or 'thinkers.iterate("mobj")'
    -- Vamos a probar con un pcall para evitar crash
    if thinkers and thinkers.iterate then
        for mo in thinkers.iterate("mobj") do
            check_obj(mo)
        end
    end

    
    if target_mo then
        local angle_to = R_PointToAngle2(p.mo.x, p.mo.y, target_mo.x, target_mo.y)
        local diff = angle_to - p.mo.angle
        -- Normalize diff
        if diff > ANGLE_180 then diff = diff - 2*ANGLE_180 end 
        
        return string.format("%s a %.1fm", obj_type, best_dist/FRACUNIT/64)
    end
    
    return "No encontrado (Iteración fallida?)"
end

local function ScanThreats(p)
    if not p.mo then return "Ninguna" end
    local min_dist = 1500 * FRACUNIT
    local closest_name = nil
    
    local function check_obj(full_obj)
        if not full_obj or not full_obj.valid then return end
        if (full_obj.flags & (MF_ENEMY|MF_BOSS)) ~= 0 and full_obj.health > 0 then
             local dist = R_PointToDist2(p.mo.x, p.mo.y, full_obj.x, full_obj.y)
             if dist < min_dist then
                 min_dist = dist
                 -- Attempt to get a readable name
                 if full_obj.info and full_obj.info.name then
                     closest_name = full_obj.info.name
                 else
                     closest_name = "Enemigo #"..tostring(full_obj.type)
                 end
             end
        end
    end
    
    searchBlockmap("objects", check_obj, p.mo)
    
    if closest_name then
        return string.format("%s a %.1fm", closest_name, min_dist/FRACUNIT/64) -- aprox meters
    end
    return "Despejado"
end

addHook("HUD", function(v)
    if (leveltime % TICRATE == 0) then
        -- print("===== REPORTE DE ESTADO DEL JUEGO =====")
        -- print("Tiempo de nivel (tics): " .. tostring(leveltime))
        -- print("Situación actual: " .. tostring(GS_MAP[gamestate] or "Desconocida"))
        -- print("Mapa número: " .. tostring(gamemap))
        
        if (consoleplayer) then
            local p = consoleplayer
            if (p.mo) then
                -- print("--- Datos del Jugador ---")
                -- print("Rings: " .. tostring(p.rings or 0))
                -- print("Vidas: " .. tostring(p.lives or 0))
                -- print("Puntuación: " .. tostring(p.score or 0))
                
                local spd = 0
                if (p.speed) then spd = p.speed / FRACUNIT end
                -- print("Velocidad actual: " .. tostring(spd))
                
                if (p.powers) then
                    local shield_name = "Ninguno"
                    if p.powers[pw_shield] and p.powers[pw_shield] > 0 then
                        shield_name = "Activo (ID:"..tostring(p.powers[pw_shield])..")"
                    end
                    -- print("Escudo: " .. shield_name)
                    -- print("Invencibilidad: " .. (p.powers[pw_invulnerability] > 0 and "SÍ" or "No"))
                    -- print("Zapatillas rápidas: " .. (p.powers[pw_sneakers] > 0 and "SÍ" or "No"))
                    -- print("Estado Super: " .. (p.powers[pw_super] > 0 and "SÍ" or "No"))
                end

                -- print("Posición: X=" .. tostring(p.mo.x/FRACUNIT) .. " Y=" .. tostring(p.mo.y/FRACUNIT))
                
                -- Advanced Metrics
                local haz = "Ninguno"
                if p.mo.subsector and p.mo.subsector.sector then
                     local sec = p.mo.subsector.sector
                     if sec.damagetype and sec.damagetype > 0 then
                         haz = "DAÑO TIPO " .. tostring(sec.damagetype)
                     end
                end
                -- print("Peligro Ambiental: " .. haz)
                
                local threat = ScanThreats(p)
                -- print("Amenaza Cercana: " .. threat)
                -- print("Objetivo: " .. FindObjective(p))
                -- print("Checkpoint: " .. tostring(p.starpostnum or 0))
                
            else
                 -- print("Jugador esperando aparecer (Spectator/Menu)...")
            end
        else
            -- print("Esperando a que el jugador entre al nivel...")
        end
        -- print("========================================")
    end
end)

print("TELEMETRY_V2: Listo y reportando.")

local resource = GetCurrentResourceName()
local catalog = json.decode(LoadResourceFile(resource, 'catalog.json') or '{}')
local preview, camera, opened, walk = nil, nil, false, nil
local origin, heading
local characterVersion = 0
local function notify(message)
    print('[Duck Studio] ' .. message)
    SendNUIMessage({ action = 'status', message = message })
end
local function close()
    opened = false; walk = nil
    characterVersion = characterVersion + 1
    SetNuiFocus(false, false)
    SendNUIMessage({ action = 'close' })
    if camera then RenderScriptCams(false, false, 0, true, true); DestroyCam(camera, false); camera = nil end
    if preview then DeleteEntity(preview); preview = nil end
end
local function character(pedName)
    if pedName ~= 'mp_m_freemode_01' and pedName ~= 'mp_f_freemode_01' then return false end
    local hash = joaat(pedName)
    if preview and GetEntityModel(preview) == hash then return true end
    characterVersion = characterVersion + 1
    local version = characterVersion
    if preview then DeleteEntity(preview); preview = nil end
    RequestModel(hash)
    local deadline = GetGameTimer() + 8000
    while not HasModelLoaded(hash) and GetGameTimer() < deadline do Wait(10) end
    if not HasModelLoaded(hash) then notify('Character model could not load.'); return false end
    if not opened or version ~= characterVersion then SetModelAsNoLongerNeeded(hash); return false end
    preview = CreatePed(4, hash, origin.x, origin.y, origin.z, heading, false, false)
    SetModelAsNoLongerNeeded(hash)
    SetEntityInvincible(preview, true); SetEntityCollision(preview, true, true)
    SetEntityNoCollisionEntity(preview, PlayerPedId(), false)
    SetBlockingOfNonTemporaryEvents(preview, true); SetPedDefaultComponentVariation(preview)
    FreezeEntityPosition(preview, true); walk = nil
    return true
end
local function applyPart(part)
    if not preview then return end
    if part.prop then
        if part.hidden then ClearPedProp(preview, part.component)
        else SetPedCollectionPropIndex(preview, part.component, part.collection, part.drawable, part.texture, true) end
    elseif not part.hidden then
        if IsPedCollectionComponentVariationValid(preview, part.component, part.collection, part.drawable, part.texture) then
            SetPedCollectionComponentVariation(preview, part.component, part.collection, part.drawable, part.texture, 0)
        else notify('Invalid variation. Ensure the built pack is running, and re-export this companion after rebuilding.') end
    else
        -- GTA has no general hide-component native. Known freemode invisible
        -- components are used for clothing inspection; other slots retain defaults.
        local female = GetEntityModel(preview) == joaat('mp_f_freemode_01')
        local invisible = { [3] = 15, [4] = female and 15 or 14, [8] = 15, [9] = 0, [10] = 0, [11] = 15 }
        if invisible[part.component] then SetPedComponentVariation(preview, part.component, invisible[part.component], 0, 0) end
    end
end
local function find(list, id)
    for _, item in ipairs(list or {}) do if item.id == id then return item end end
end
RegisterCommand('duckstudio', function()
    if opened then close(); return end
    local player = PlayerPedId()
    origin = GetOffsetFromEntityInWorldCoords(player, 0.0, 2.0, 0.0); heading = GetEntityHeading(player)
    opened = true
    if not character('mp_m_freemode_01') then close(); return end
    camera = CreateCam('DEFAULT_SCRIPTED_CAMERA', true)
    SetCamFov(camera, 40.0); RenderScriptCams(true, false, 0, true, true)
    SetNuiFocus(true, true)
    SendNUIMessage({ action = 'open', catalog = catalog })
end, false)
RegisterNUICallback('close', function(_, cb) close(); cb({ok=true}) end)
RegisterNUICallback('item', function(data, cb)
    local item = find(catalog.clothing, data.id)
    if opened and item and character(item.ped) then
        local texture = math.floor(tonumber(data.texture) or 0)
        if texture >= 0 and texture < item.textures then
            applyPart({prop=item.prop,component=item.component,collection=item.collection,drawable=item.drawable,texture=texture})
            notify(item.name .. ' / texture ' .. texture)
        end
    end
    cb({ok=true})
end)
RegisterNUICallback('outfit', function(data, cb)
    local outfit = find(catalog.outfits, data.id)
    if opened and outfit and character(outfit.ped) then
        SetPedDefaultComponentVariation(preview); ClearAllPedProps(preview); ClearPedDecorations(preview)
        for _, part in ipairs(outfit.parts or {}) do applyPart(part) end
        for _, id in ipairs(outfit.tattoos or {}) do
            local tattoo = find(catalog.tattoos, id)
            local preset = tattoo and (outfit.ped == 'mp_f_freemode_01' and tattoo.female or tattoo.male)
            if preset then AddPedDecorationFromHashes(preview, joaat(tattoo.collection), joaat(preset)) end
        end
        notify('Applied ' .. outfit.name)
    end
    cb({ok=true})
end)
RegisterNUICallback('tattoo', function(data, cb)
    local tattoo = find(catalog.tattoos, data.id)
    local female = data.gender == 'female'
    if opened and tattoo and character(female and 'mp_f_freemode_01' or 'mp_m_freemode_01') then
        local preset = female and tattoo.female or tattoo.male
        if preset then AddPedDecorationFromHashes(preview, joaat(tattoo.collection), joaat(preset)); notify(tattoo.name)
        else notify('This tattoo has no preset for that character.') end
    end
    cb({ok=true})
end)
RegisterNUICallback('clearTattoos', function(_, cb) if preview then ClearPedDecorations(preview) end; cb({ok=true}) end)
RegisterNUICallback('pose', function(data, cb)
    if preview and opened then
        walk = nil; ClearPedTasksImmediately(preview); RemoveAllPedWeapons(preview, true)
        SetEntityCoordsNoOffset(preview, origin.x, origin.y, origin.z, false, false, false); SetEntityHeading(preview, heading)
        FreezeEntityPosition(preview, data.pose == 'standing')
        if data.pose == 'walking' or data.pose == 'running' then
            walk = { speed = data.pose == 'running' and 2.5 or 1.0, direction = 1, next = 0 }
        elseif data.pose == 'sitting' then TaskStartScenarioInPlace(preview, 'PROP_HUMAN_SEAT_CHAIR_MP_PLAYER', 0, true)
        elseif data.pose == 'aiming' then
            GiveWeaponToPed(preview, joaat('WEAPON_PISTOL'), 30, false, true)
            local target = GetOffsetFromEntityInWorldCoords(preview, 0.0, 6.0, 1.3)
            TaskAimGunAtCoord(preview, target.x, target.y, target.z, -1, false, false)
        end
    end
    cb({ok=true})
end)
CreateThread(function()
    while true do
        if opened and preview then
            local position = GetEntityCoords(preview)
            if camera then
                local offset = GetOffsetFromEntityInWorldCoords(preview, 0.0, 3.7, 1.0)
                SetCamCoord(camera, offset.x, offset.y, offset.z)
                PointCamAtEntity(camera, preview, 0.0, 0.0, 0.6, true)
            end
            if walk and GetGameTimer() >= walk.next then
                local radians = math.rad(heading)
                local target = vector3(origin.x - math.sin(radians) * 2.0 * walk.direction, origin.y + math.cos(radians) * 2.0 * walk.direction, origin.z)
                TaskGoStraightToCoord(preview, target.x, target.y, target.z, walk.speed, -1, heading, 0.1)
                walk.next = GetGameTimer() + math.floor(4000 / walk.speed); walk.direction = -walk.direction
            end
            Wait(50)
        else Wait(500) end
    end
end)
AddEventHandler('onResourceStop', function(name) if name == resource then close() end end)

#include <amxmodx>
#include <fun>
#include <engine>
#include <fakemeta>
#include <cstrike>
#include <vector>

#define CONTROL_FILE "addons/amxmodx/configs/offline_trainer.ctrl"
#define TELEMETRY_FILE "addons/amxmodx/configs/offline_trainer.telemetry"
#define TELEMETRY_TMP "addons/amxmodx/configs/offline_trainer.tmp"
#define CONTROL_V2 "addons/amxmodx/configs/offline_trainer_v2.ctrl"

new g_sequence = -1;
new bool:g_health, bool:g_ammo, bool:g_glow, bool:g_aim;
new bool:g_glow_enemy, bool:g_glow_team;
new g_aim_fov = 120, g_aim_part = 0, g_aim_smooth = 7;
new g_aim_width = 800;
new bool:g_aim_through_wall;
new g_enemy_color[3] = {255, 65, 40}, g_team_color[3] = {45, 120, 255}, g_glow_brightness = 25;
new g_random_part[33];
new Float:g_last_aim_time[33], Float:g_last_telemetry;
new g_settings_sequence = -1;
new g_roundtime = 0;
new g_model_bones[33][18];
new g_model_signature[33][64];
new const g_bone_names[18][] = {
    "Bip01 Head", "Bip01 Neck", "Bip01 Spine3", "Bip01 Pelvis",
    "Bip01 L Clavicle", "Bip01 L UpperArm", "Bip01 L Forearm", "Bip01 L Hand",
    "Bip01 R Clavicle", "Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand",
    "Bip01 L Thigh", "Bip01 L Calf", "Bip01 L Foot",
    "Bip01 R Thigh", "Bip01 R Calf", "Bip01 R Foot"
};

stock bool:ensure_model_bones(id) {
    new model[64];
    cs_get_user_model(id, model, charsmax(model));
    if (!model[0]) return false;
    if (equal(model, g_model_signature[id])) return true;
    new path[128], file;
    formatex(path, charsmax(path), "models/player/%s/%s.mdl", model, model);
    file = fopen(path, "rb");
    if (!file) { log_amx("Bone model not readable: %s", path); return false; }
    new bone_count, bone_offset;
    fseek(file, 140, SEEK_SET); fread(file, bone_count, BLOCK_INT);
    fread(file, bone_offset, BLOCK_INT);
    if (bone_count < 18 || bone_count > 256 || bone_offset < 200 || bone_offset > 100000) { fclose(file); return false; }
    new name[33], ch, matched = 0;
    for (new i = 0; i < 18; i++) g_model_bones[id][i] = -1;
    for (new b = 0; b < bone_count; b++) {
        fseek(file, bone_offset + b * 112, SEEK_SET);
        for (new c = 0; c < 32; c++) {
            fread(file, ch, BLOCK_CHAR);
            name[c] = ch;
        }
        name[32] = 0;
        for (new j = 0; j < 18; j++) if (equal(name, g_bone_names[j])) { g_model_bones[id][j] = b; matched++; break; }
    }
    fclose(file);
    if (matched != 18) { log_amx("Bone map failed for %s (%d/18)", model, matched); return false; }
    copy(g_model_signature[id], charsmax(g_model_signature[]), model);
    return true;
}

public plugin_init() {
    register_plugin("Offline Trainer", "2.2", "Codex");
    g_roundtime = get_cvar_num("mp_roundtime");
    set_task(0.20, "poll_control", 2024, "", 0, "b");
    set_task(0.10, "poll_advanced", 2027, "", 0, "b");
    set_task(0.10, "apply_features", 2025, "", 0, "b");
    register_forward(FM_StartFrame, "on_start_frame");
    register_forward(FM_CmdStart, "on_cmd_start");
    register_event("HLTV", "on_round_start", "a", "1=0", "2=0");
    log_amx("Offline Trainer loaded");
}

public on_round_start() {
    for (new i = 1; i <= 32; i++) g_random_part[i] = random_num(0, 7);
}

public on_start_frame() {
    new Float:now = get_gametime();
    // File-backed telemetry is synchronous; cap disk work to 20 Hz so it
    // doesn't compete with rendering while still keeping ESP responsive.
    if (now - g_last_telemetry >= 0.050) {
        g_last_telemetry = now;
        write_telemetry();
    }
}

public write_telemetry() {
    new players[32], count, id = 0;
    get_players(players, count, "a");
    for (new i = 0; i < count; i++) {
        if (!is_user_bot(players[i])) { id = players[i]; break; }
    }
    if (!id) return;
    new f = fopen(TELEMETRY_TMP, "wt");
    if (!f) return;
    new Float:eye[3], Float:ofs[3], Float:angles[3], Float:fov;
    pev(id, pev_origin, eye);
    pev(id, pev_view_ofs, ofs);
    for (new k = 0; k < 3; k++) eye[k] += ofs[k];
    pev(id, pev_v_angle, angles);
    new Float:punch[3];
    pev(id, pev_punchangle, punch);
    angles[0] += punch[0];
    angles[1] += punch[1];
    pev(id, pev_fov, fov);
    new clip, reserve, weapon = get_user_weapon(id, clip, reserve);
    fprintf(f, "2 %f %f %f %f %f %f %d %d %d %d %d %d %f^n", eye[0], eye[1], eye[2], angles[0], angles[1], fov, get_user_health(id), weapon, clip, reserve, get_cvar_num("mp_roundtime"), get_user_team(id), get_gametime());
    for (new enemy = 1; enemy <= 32; enemy++) {
        if (enemy == id || !is_user_alive(enemy)) continue;
        new Float:origin[3], Float:mins[3], Float:maxs[3];
        new name[32], clip2, reserve2;
        pev(enemy, pev_origin, origin);
        pev(enemy, pev_mins, mins);
        pev(enemy, pev_maxs, maxs);
        get_user_name(enemy, name, charsmax(name));
        replace(name, charsmax(name), " ", "_");
        get_user_weapon(enemy, clip2, reserve2);
        fprintf(f, "%d %f %f %f %f %f %f %f %f %f %d %d %d %d %s", enemy, origin[0], origin[1], origin[2], mins[0], mins[1], mins[2], maxs[0], maxs[1], maxs[2], get_user_health(enemy), clip2, reserve2, get_user_team(enemy), name);
        // Ordered anatomical joints resolved from this player's actual model.
        if (!ensure_model_bones(enemy)) { fprintf(f, "^n"); continue; }
        new Float:bone[3];
        for (new j = 0; j < 18; j++) {
            if (!studio_bone_world(enemy, j, bone)) break;
            fprintf(f, " %f %f %f", bone[0], bone[1], bone[2]);
        }
        fprintf(f, "^n");
    }
    fclose(f);
    // Replace in one rename operation. Deleting the live file first creates
    // a brief missing-file window on every telemetry tick and can make ESP
    // appear to blink when the renderer polls during that gap.
    rename_file(TELEMETRY_TMP, TELEMETRY_FILE, 1);
}

public poll_advanced() {
    new f = fopen(CONTROL_V2, "rt");
    if (!f) return;
    new line[256], seq[16], enemy[8], team[8], aim[8], fov[8], part[8], smooth[8], width[8], through[8];
    new er[8], eg[8], eb[8], tr[8], tg[8], tb[8], brightness[8];
    fgets(f, line, charsmax(line));
    fclose(f);
    new fields = parse(line, seq, charsmax(seq), enemy, charsmax(enemy), team, charsmax(team), aim, charsmax(aim), fov, charsmax(fov), part, charsmax(part), smooth, charsmax(smooth), width, charsmax(width), through, charsmax(through), er, charsmax(er), eg, charsmax(eg), eb, charsmax(eb), tr, charsmax(tr), tg, charsmax(tg), tb, charsmax(tb), brightness, charsmax(brightness));
    if (fields < 7) return;
    new current = str_to_num(seq);
    if (current == g_settings_sequence) return;
    g_settings_sequence = current;
    g_glow_enemy = str_to_num(enemy) != 0;
    g_glow_team = str_to_num(team) != 0;
    g_aim = str_to_num(aim) != 0;
    g_aim_fov = clamp(str_to_num(fov), 20, 600);
    g_aim_part = clamp(str_to_num(part), 0, 8);
    g_aim_smooth = clamp(str_to_num(smooth), 1, 30);
    g_aim_width = (fields >= 8) ? clamp(str_to_num(width), 320, 7680) : 800;
    g_aim_through_wall = (fields >= 9) && str_to_num(through) != 0;
    if (fields >= 16) {
        g_enemy_color[0] = clamp(str_to_num(er), 0, 255);
        g_enemy_color[1] = clamp(str_to_num(eg), 0, 255);
        g_enemy_color[2] = clamp(str_to_num(eb), 0, 255);
        g_team_color[0] = clamp(str_to_num(tr), 0, 255);
        g_team_color[1] = clamp(str_to_num(tg), 0, 255);
        g_team_color[2] = clamp(str_to_num(tb), 0, 255);
        g_glow_brightness = clamp(str_to_num(brightness), 1, 100);
    }
    log_amx("Advanced seq=%d enemy_glow=%d team_glow=%d aim=%d fov=%d part=%d smooth=%d", current, g_glow_enemy, g_glow_team, g_aim, g_aim_fov, g_aim_part, g_aim_smooth);
}

public poll_control() {
    new f = fopen(CONTROL_FILE, "rt");
    if (!f) return;
    new line[128], s[16], h[8], a[8], g[8], aim[8], minutes[8], reset[8];
    fgets(f, line, charsmax(line));
    fclose(f);
    if (parse(line, s, charsmax(s), h, charsmax(h), a, charsmax(a), g, charsmax(g), aim, charsmax(aim), minutes, charsmax(minutes), reset, charsmax(reset)) != 7) return;
    new seq = str_to_num(s);
    if (seq == g_sequence) return;
    g_sequence = seq;
    g_health = str_to_num(h) != 0;
    g_ammo = str_to_num(a) != 0;
    g_glow = str_to_num(g) != 0;
    if (g_settings_sequence < 0) {
        g_glow_enemy = g_glow;
        g_glow_team = g_glow;
        g_aim = str_to_num(aim) != 0;
    }
    new newtime = str_to_num(minutes);
    if (newtime > 0 && newtime <= 60 && newtime != g_roundtime) {
        g_roundtime = newtime;
        set_cvar_num("mp_roundtime", newtime);
        server_cmd("sv_restartround 1");
    }
    if (str_to_num(reset)) server_cmd("sv_restartround 1");
    log_amx("Offline Trainer seq=%d health=%d ammo=%d glow=%d aim=%d time=%d reset=%d", seq, g_health, g_ammo, g_glow, g_aim, newtime, str_to_num(reset));
}

public apply_features() {
    new players[32], count;
    get_players(players, count, "a");
    for (new i = 0; i < count; i++) {
        new id = players[i];
        new bool:should_glow;
        if (is_user_bot(id)) {
            new own_team = 0;
            for (new p = 0; p < count; p++) if (!is_user_bot(players[p])) { own_team = get_user_team(players[p]); break; }
            should_glow = (get_user_team(id) == own_team) ? g_glow_team : g_glow_enemy;
        }
        if (should_glow) {
            if (get_user_team(id) == own_team_for_glow()) set_user_rendering(id, kRenderFxGlowShell, g_team_color[0], g_team_color[1], g_team_color[2], kRenderNormal, g_glow_brightness);
            else set_user_rendering(id, kRenderFxGlowShell, g_enemy_color[0], g_enemy_color[1], g_enemy_color[2], kRenderNormal, g_glow_brightness);
        } else set_user_rendering(id);
        if (is_user_bot(id)) continue;
        set_user_godmode(id, g_health);
        if (g_health && get_user_health(id) < 100) set_user_health(id, 100);
        if (g_ammo) replenish_ammo(id);
    }
}

stock own_team_for_glow() {
    for (new i = 1; i <= 32; i++) if (is_user_connected(i) && !is_user_bot(i)) return get_user_team(i);
    return 0;
}

stock bool:bone_target(other, part, Float:point[3]) {
    if (!ensure_model_bones(other)) return false;
    // The menu sends a zero-based enum. Keep this table in the same order as
    // the UI (head, neck, L/R hand, chest, L/R leg, body) so each choice maps
    // directly to the named joint slot.
    static const bone_slot[8] = {0, 1, 7, 11, 2, 12, 15, 3};
    static const labels[9][] = {"head", "neck", "left_hand", "right_hand", "chest", "left_leg", "right_leg", "body", "random"};
    if (part < 0 || part > 7) return false;
    new index = bone_slot[part];
    static g_last_logged_part = -1;
    if (g_last_logged_part != part) { log_amx("Aim selection=%s ui_part=%d slot=%d model_bone=%d model=%s", labels[part], part, index, g_model_bones[other][index], g_model_signature[other]); g_last_logged_part = part; }
    if (!studio_bone_world(other, index, point)) return false;
    return !(point[0] == 0.0 && point[1] == 0.0 && point[2] == 0.0);
}

stock bool:studio_bone_world(id, joint, Float:point[3]) {
    if (g_model_bones[id][joint] < 0) return false;
    new Float:ang[3];
    engfunc(EngFunc_GetBonePosition, id, g_model_bones[id][joint], point, ang);
    return true;
}

stock bool:aim_visible(id, other, const Float:from[3], const Float:to[3]) {
    engfunc(EngFunc_TraceLine, from, to, 0, id, 0);
    new Float:fraction;
    get_tr2(0, TR_flFraction, fraction);
    return fraction >= 0.999 || get_tr2(0, TR_pHit) == other;
}

stock replenish_ammo(id) {
    new weapons[32], count, weapon_name[40];
    get_user_weapons(id, weapons, count);
    for (new i = 0; i < count; i++) {
        new wid = weapons[i];
        if (wid == CSW_KNIFE || wid == CSW_C4 || wid == CSW_HEGRENADE || wid == CSW_FLASHBANG || wid == CSW_SMOKEGRENADE) continue;
        cs_set_user_bpammo(id, wid, 250);
    }
    new active = get_user_weapon(id);
    if (active <= 0 || active == CSW_KNIFE || active == CSW_C4 || active == CSW_HEGRENADE || active == CSW_FLASHBANG || active == CSW_SMOKEGRENADE) return;
    get_weaponname(active, weapon_name, charsmax(weapon_name));
    new ent = find_ent_by_owner(-1, weapon_name, id);
    if (ent > 0) cs_set_weapon_ammo(ent, 30);
}

public on_cmd_start(id, uc_handle, random_seed) {
    if (!g_aim || !is_user_alive(id) || is_user_bot(id)) return FMRES_IGNORED;
    static bool:g_aim_probe_logged;
    if (!g_aim_probe_logged) { log_amx("Aim hook active: selected anatomical bone drives both target score and steering."); g_aim_probe_logged = true; }
    new Float:eye[3], Float:ofs[3], Float:view[3], Float:origin[3], Float:delta[3], Float:desired[3], Float:target_point[3];
    pev(id, pev_origin, eye);
    pev(id, pev_view_ofs, ofs);
    for (new k = 0; k < 3; k++) eye[k] += ofs[k];
    get_uc(uc_handle, UC_ViewAngles, view);
    new Float:punch[3];
    pev(id, pev_punchangle, punch);
    view[0] += punch[0]; view[1] += punch[1];
    new Float:fov, Float:best = 999999.0;
    pev(id, pev_fov, fov);
    if (fov <= 1.0) fov = 90.0;
    new Float:half_fov = fov * 0.00872664626;
    new Float:scale = float(g_aim_width) * floatcos(half_fov) / (2.0 * floatsin(half_fov));
    new Float:yaw_rad = view[1] * 0.01745329252;
    new Float:pitch_rad = view[0] * 0.01745329252;
    new Float:cy = floatcos(yaw_rad), Float:sy = floatsin(yaw_rad);
    new Float:cp = floatcos(pitch_rad), Float:sp = floatsin(pitch_rad);
    new best_id = 0, best_part = g_aim_part;
    for (new other = 1; other <= 32; other++) {
        if (other == id || !is_user_alive(other) || get_user_team(other) == get_user_team(id)) continue;
        new part = (g_aim_part == 8) ? g_random_part[other] : g_aim_part;
        if (part < 0 || part > 7) part = 0;
        if (!bone_target(other, part, origin)) continue;
        if (!g_aim_through_wall && !aim_visible(id, other, eye, origin)) continue;
        for (new k = 0; k < 3; k++) delta[k] = origin[k] - eye[k];
        new Float:depth = delta[0] * cp * cy + delta[1] * cp * sy - delta[2] * sp;
        if (depth < 5.0) continue;
        new Float:right = delta[0] * sy - delta[1] * cy;
        new Float:up = delta[0] * sp * cy + delta[1] * sp * sy + delta[2] * cp;
        new Float:sx = right * scale / depth, Float:sy_screen = up * scale / depth;
        new Float:score = sx * sx + sy_screen * sy_screen;
        if (score > float(g_aim_fov * g_aim_fov) || score >= best) continue;
        best = score;
        best_id = other;
        best_part = part;
        for (new k = 0; k < 3; k++) desired[k] = target_point[k] = origin[k];
    }
    if (!best_id) return FMRES_IGNORED;
    // Scoring and steering use the same measured bone point for this command.
    for (new k = 0; k < 3; k++) delta[k] = desired[k] - eye[k];
    // GoldSrc view pitch is negative when looking up. Compute it explicitly
    // so the selected limb is not driven toward a different screen location.
    new Float:horizontal = floatsqroot(delta[0] * delta[0] + delta[1] * delta[1]);
    desired[0] = -floatatan2(delta[2], horizontal, degrees);
    desired[1] = floatatan2(delta[1], delta[0], degrees);
    new Float:now = get_gametime();
    new Float:dt = floatclamp(now - g_last_aim_time[id], 0.001, 0.10);
    g_last_aim_time[id] = now;
    new Float:factor = (g_aim_smooth <= 1) ? 1.0 : dt / (dt + float(g_aim_smooth - 1) * 0.020);
    view[0] += normalize_angle(desired[0] - view[0]) * factor;
    view[1] += normalize_angle(desired[1] - view[1]) * factor;
    view[0] -= punch[0]; view[1] -= punch[1];
    // FM_CmdStart owns this user command. Repeatedly writing pev_v_angle and
    // setting pev_fixangle here makes GoldSrc resnap the camera every command,
    // which amplifies small bone-position changes into visible ESP jitter.
    set_uc(uc_handle, UC_ViewAngles, view);
    static Float:g_last_aim_diag;
    new Float:diag_now = get_gametime();
    if (diag_now - g_last_aim_diag >= 1.0) {
        log_amx("Aim steering target=%d part=%d point=(%.1f %.1f %.1f) score=%.1f angle=(%.1f %.1f)", best_id, best_part, target_point[0], target_point[1], target_point[2], best, view[0], view[1]);
        g_last_aim_diag = diag_now;
    }
    return FMRES_HANDLED;
}


stock Float:normalize_angle(Float:value) {
    while (value > 180.0) value -= 360.0;
    while (value < -180.0) value += 360.0;
    return value;
}


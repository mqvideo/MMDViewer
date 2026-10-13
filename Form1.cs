using MMDTools;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using BulletSharp;
using OG4 = OpenTK.Graphics.OpenGL4;
using OM4 = OpenTK.Mathematics.Matrix4;
using OMH = OpenTK.Mathematics.MathHelper;
using OV3 = OpenTK.Mathematics.Vector3;
using OV4 = OpenTK.Mathematics.Vector4;
using OQ = OpenTK.Mathematics.Quaternion;
using MV3 = MMDTools.Vector3;
using MV4 = MMDTools.Vector4;
using MRB = MMDTools.RigidBody;
using BRB = BulletSharp.RigidBody;
using EM4 = Evergine.Mathematics.Matrix4x4;
using EV3 = Evergine.Mathematics.Vector3;
namespace MMDViewer
{
    public partial class Form1 : Form
    {
        private PMXObject? model;
        private string file_path = "";
        // 模型自检
        private int bad_verts;      // NaN/Inf 废顶点数
        private int outlier_verts;  // 远离本体的离群顶点数（取景时忽略，仍会渲染）
        private int isolated_verts; // 孤立顶点数（未被任何面引用）
        private int missing_tex;    // 被材质引用但文件不存在的纹理数
        private readonly List<string> missing_tex_names = new();
        private int failed_tex;     // 被材质引用、文件存在但加载失败（格式不支持/损坏）的纹理数
        private readonly List<string> failed_tex_names = new();
        private int disabled_render_tex; // 被材质引用、加载失败、但渲染模式为Disabled的纹理数
        private readonly List<string> disabled_render_tex_names = new();
        private int unref_tex;      // 未被任何材质引用的纹理数（不参与渲染。例如：Vroid导出的法线贴图/matcap等常见于此）
        private readonly List<string> unref_tex_names = new();
        private readonly List<string> model_issues = new();
        private string model_issue_summary = "";
        // OpenGL渲染资源
        private int shader_program;                             // 着色器程序ID
        private int position_vbo, normal_vbo, uv_vbo;           // 顶点位置/法线/UV的VBO
        private int bone_index_vbo, bone_weight_vbo;            // 骨骼索引/权重的VBO
        private int bone_buffer;                                // 骨骼矩阵数据缓冲区（Texture Buffer后端）
        private int bone_texture;                               // 缓冲纹理对象（samplerBuffer）
        private OM4[] bone_tex_buf = Array.Empty<OM4>();        // 骨骼矩阵上传缓冲区（转置后行主序连续存放 = GLSL列主序mat4）
        private int bone_tex_capacity;                          // 缓冲纹理当前可容纳的骨骼数
        private int ebo;                                        // 索引缓冲区
        private int vao;                                        // 顶点数组对象
        private readonly Dictionary<int, int> textures = new(); // 纹理缓存
        private readonly Dictionary<int, int> toon_textures = new(); // toon纹理缓存
        private int default_texture;       // 纯白1x1纹理，材质无纹理时的fallback
        private int default_toon_texture;  // 默认渐变toon纹理，材质未指定toon时使用
        private readonly List<(int first_index, int index_count, Material material)> draw_groups = new(); // 按材质分组的渲染批次 (EBO字节偏移, 顶点数, 材质信息)
        // 相机参数
        private OM4 projection = OM4.CreatePerspectiveFieldOfView(OMH.DegreesToRadians(30f), 1f, 0.1f, 5000f); // 30度FOV, 近/远裁面在update_projection中随相机距离自适应
        private float camera_distance = 45f;   // 相机到目标的距离
        private Quaternion camera_rotation = Quaternion.Identity;  // 轨道旋转四元数
        private OV3 model_center = OV3.Zero;    // 模型包围盒中心
        private float model_size = 1f;          // 模型包围盒最大边长，用于自动缩放相机距离
        private float view_target_y;            // 相机默认注视点Y
        private OV3 camera_target = OV3.Zero;   // 相机注视点
        private bool mouse_left_down, mouse_right_down, mouse_middle_down;
        private Point last_mouse_pos;
        private bool lighting_enabled;  // checkbox控制是否启用逐像素光照
        // 缓存uniform位置，避免每帧查询GL状态
        private int uloc_mvp, uloc_model, uloc_view, uloc_light_pos, uloc_light_color, uloc_view_pos;
        private int uloc_tex_sampler, uloc_obj_color, uloc_spec_color, uloc_shininess;
        private int uloc_sphere_mode, uloc_has_toon, uloc_mat_ambient, uloc_use_lighting;
        private int uloc_sphere_sampler, uloc_toon_sampler;
        private int uloc_has_main_tex;
        private int uloc_bone_tex, uloc_bone_tex_width;
        // 动画状态
        private vmd_motion? current_motion;
        private vmd_motion? camera_motion;
        private float camera_fov = 30f;        // 投影垂直FOV（度），镜头动画可修改，默认30
        private bool anim_playing, anim_paused;
        private bool display_rest_pose = true; // 非播放状态下的静止姿势来源。true = rest pose（停止/未加载动作），false = 当前动画帧（刚导入动作/暂停/播完）
        private float current_frame;
        private float anim_frame_offset;   // 播放/继续时的起始帧（current_frame = offset + 时钟*30）
        private Stopwatch anim_clock = new Stopwatch();
        private Stopwatch frame_timer = new Stopwatch(); // 帧率限制器
        private bool render_busy;
        private Stopwatch advance_timer = new Stopwatch(); // 距上次帧推进的实际间隔，用于检测播放被挂起
        private const double suspend_gap_ms = 250.0; // 间隔超过此值视为挂起（模态弹窗/拖动窗口等），正常帧仅约7ms
        private const double target_frame_ms = 1000.0 / 144.0; // 144fps上限
        private int anim_fps_count;
        private DateTime anim_fps_time = DateTime.Now;
        private int anim_current_fps;
        private OM4[] bone_rest_local = Array.Empty<OM4>();   // 骨骼rest pose局部变换
        private OQ[] local_rot_buf = Array.Empty<OQ>();       // 每帧FK后的局部旋转四元数（IK可修改）
        private List<ik_solver_info> ik_solvers = new();       // IK解算器列表
        private Dictionary<string, List<bone_keyframe>> bone_frame_map = new Dictionary<string, List<bone_keyframe>>();
        private HashSet<string> movable_bones = new HashSet<string>();
        // 表情（morph，变形）
        // VMD morph关键帧驱动顶点/UV变形。mesh GPU蒙皮前先在bind pose施加morph偏移
        private float[] morph_base_pos = Array.Empty<float>();   // 基态顶点位置
        private float[] morph_base_uv = Array.Empty<float>();    // 基态UV
        private float[] morph_work_pos = Array.Empty<float>();   // 每帧施加morph后的工作缓冲
        private float[] morph_work_uv = Array.Empty<float>();    // 工作UV
        private Dictionary<string, int> morph_name_index = new Dictionary<string, int>(); // morph名、模型morph索引
        private bool has_morphs;             // 模型是否含morph
        private bool has_bone_morph;         // 模型是否含骨骼morph（少数模型用骨移动做表情）
        private bool has_material_morph;     // 模型是否含材质morph（颜色/显隐，如脸红）
        // 骨骼morph
        private OV3[] bone_morph_trans = Array.Empty<OV3>();
        private OQ[] bone_morph_rot = Array.Empty<OQ>();
        // 材质morph
        private OV4[] matm_diffuse_mul = Array.Empty<OV4>();
        private OV4[] matm_diffuse_add = Array.Empty<OV4>();
        private OV3[] matm_specular_mul = Array.Empty<OV3>();
        private OV3[] matm_specular_add = Array.Empty<OV3>();
        private float[] matm_shininess_mul = Array.Empty<float>();
        private float[] matm_shininess_add = Array.Empty<float>();
        private OV3[] matm_ambient_mul = Array.Empty<OV3>();
        private OV3[] matm_ambient_add = Array.Empty<OV3>();
        private bool matm_active;             // 本帧材质morph是否生效
        private bool morph_applied;           // 当前VBO是否持有morph后数据
        private float last_morph_frame = -1f; // 上次施加morph的帧号
        private OM4[] bone_rest_world = Array.Empty<OM4>(); // rest pose世界变换（用于计算逆绑定）
        private OM4[] bone_skinning = Array.Empty<OM4>();   // 蒙皮矩阵
        private int[] bone_parent = Array.Empty<int>();      // 父骨骼索引
        private OM4[] bone_inv_rest = Array.Empty<OM4>();    // rest pose世界变换的逆矩阵（预计算，避免每帧重复求逆）
        // 每帧骨骼计算的可复用缓冲区（避免GC压力）
        private OM4[] animated_local_buf = Array.Empty<OM4>();
        private OM4[] animated_world_buf = Array.Empty<OM4>();
        private bool[] done_buf = Array.Empty<bool>();
        private int[] bone_topology = Array.Empty<int>();     // 骨骼拓扑序（父在子之前），用于单遍传播
        // 继承源（append/grant）与MMD变形顺序
        private int[] bone_append_source = Array.Empty<int>();   // 继承源骨索引
        private float[] bone_append_ratio = Array.Empty<float>(); // 继承比例
        private bool[] bone_append_rot = Array.Empty<bool>();     // 继承旋转
        private bool[] bone_append_pos = Array.Empty<bool>();     // 继承位移
        private OV3[] bone_anim_offset = Array.Empty<OV3>();      // 每帧FK位置偏移
        private int[] bone_transform_depth = Array.Empty<int>();  // PMX TransformDepth
        private int[] bone_after_physics = Array.Empty<int>();    // 是否TransformAfterPhysics(0/1)
        private int[] deform_order = Array.Empty<int>();          // MMD变形顺序(afterPhysics,depth,index)
        private ik_solver_info?[] bone_ik_solver = Array.Empty<ik_solver_info?>(); // 骨索引、IK解算器
        // 物理引擎
        private DefaultCollisionConfiguration? phys_config;
        private CollisionDispatcher? phys_dispatcher;
        private DbvtBroadphase? phys_broadphase;
        private SequentialImpulseConstraintSolver? phys_solver;
        private DiscreteDynamicsWorld? phys_world;
        private BRB? phys_ground; // 地面刚体（y = 0平面，模拟MMD的地面物理）
        private List<BRB> phys_bodies = new List<BRB>();
        private List<CollisionShape> phys_shapes = new List<CollisionShape>();
        private List<TypedConstraint> phys_constraints = new List<TypedConstraint>();
        private int[] phys_body_bone = Array.Empty<int>();       // 刚体对应骨骼索引（-1为无关联骨骼的自由刚体）
        private int[] phys_body_mode = Array.Empty<int>();       // 0 = 跟随骨骼、1 = 物理演算、2 = 物理+骨骼位置（katwat修正后）
        private OM4[] phys_body_offset = Array.Empty<OM4>();     // 骨骼空间到刚体空间固定偏移（刚体初始世界×骨骼逆绑定）
        private OM4[] phys_body_offset_inv = Array.Empty<OM4>(); // 上者的逆（物理结果写回骨骼用）
        private OM4[] phys_body_initial = Array.Empty<OM4>();    // 刚体初始世界变换（GL系），无骨刚体重置用
        private OM4[] phys_bone_world = Array.Empty<OM4>();      // 每帧物理结果的骨骼世界矩阵
        private byte[] phys_bone_flag = Array.Empty<byte>();     // 每帧标记（0 = 无物理、1 = 全量物理、2 = 仅旋转物理）
        // 焊接对
        private List<int> phys_weld_a = new List<int>();         // A侧刚体（六轴全锁死关节）
        private List<int> phys_weld_b = new List<int>();         // B侧刚体（每帧刚性跟随A，不建约束）
        private List<OM4> phys_weld_rel = new List<OM4>();       // B相对A的固定位姿（rest pose时的 initial_b × initial_a^-1）
        private bool phys_needs_reset;   // 开始播放/停止/播放中勾选后需要把刚体传送到当前骨骼位姿并清零速度
        // 着色器程序，一般情况下不需要动，除非你知道你在做什么。部分代码参考了MikuMikuFlex（MMF），稍有改动
        // 顶点着色器
        // 将模型坐标变换到裁剪空间，传递法线/UV/视空间法线给片元着色器。骨骼矩阵通过缓冲纹理上传。
        private const string vertex_shader = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV;
layout(location=3) in ivec4 aBoneIndex;
layout(location=4) in vec4 aBoneWeight;
out vec3 vNormal;
out vec3 vFragPos;
out vec2 vUV;
out vec3 vViewNormal;
uniform samplerBuffer boneTex;
uniform int boneTexWidth;
mat4 fetchBone(int idx) {
    int base = idx * 4;
    return mat4(texelFetch(boneTex, base), texelFetch(boneTex, base + 1),
                texelFetch(boneTex, base + 2), texelFetch(boneTex, base + 3));
}
uniform mat4 mvp;
uniform mat4 model;
uniform mat4 view;
void main(){
    vec4 skinnedPos = vec4(0.0);
    vec4 skinnedNrm = vec4(0.0);
    for (int i = 0; i < 4; i++) {
        float w = aBoneWeight[i];
        if (w > 0.001) {
            int idx = aBoneIndex[i];
            if (idx >= 0 && idx < boneTexWidth) {
                mat4 bm = fetchBone(idx);
                skinnedPos += w * (vec4(aPos,1.0) * bm);
                skinnedNrm += w * (vec4(aNormal,0.0) * bm);
            }
        }
    }
    if (aBoneWeight.x + aBoneWeight.y + aBoneWeight.z + aBoneWeight.w < 0.001) {
        skinnedPos = vec4(aPos, 1.0);
        skinnedNrm = vec4(aNormal, 0.0);
    }
    vec4 worldPos = model * skinnedPos;
    gl_Position = skinnedPos * mvp;
    vNormal = normalize(mat3(model) * skinnedNrm.xyz);
    vFragPos = worldPos.xyz;
    vUV = aUV;
    vViewNormal = mat3(view) * vNormal;
}";
        // 片元着色器
        private const string frag_shader = @"#version 330 core
in vec3 vNormal;
in vec3 vFragPos;
in vec2 vUV;
in vec3 vViewNormal;
out vec4 fragColor;
uniform vec3 lightPos;
uniform vec3 lightColor;
uniform vec3 viewPos;
uniform vec4 objColor;
uniform vec3 specColor;
uniform float shininess;
uniform sampler2D texSampler;
uniform sampler2D sphereSampler;
uniform sampler2D toonSampler;
uniform int sphereMode;
uniform int hasToon;
uniform int hasMainTex;
uniform vec3 matAmbient;
uniform int useLighting;
void main(){
    vec4 texColor = texture(texSampler, vUV);
    float alpha = texColor.a * objColor.a;
    if (alpha < 0.1) { discard; }
    vec2 sphereUV = normalize(vViewNormal).xy * 0.5 + 0.5;
    vec4 sphTex = texture(sphereSampler, sphereUV);
    if (useLighting == 0) {
        vec3 baseCol = clamp(objColor.rgb + matAmbient, 0.0, 1.0);
        float baseLum = dot(baseCol, vec3(0.333));
        float fshade = 1.0;
        if (hasToon == 1 && hasMainTex == 0 && baseLum < 0.75) {
            vec3 fnorm = normalize(vNormal);
            vec3 flightDir = normalize(lightPos - vFragPos);
            float fndl = max(dot(fnorm, flightDir), 0.0);
            fshade = dot(texture(toonSampler, vec2(0.5, 1.0 - (fndl * 0.5 + 0.5))).rgb, vec3(0.333));
        }
        vec3 matColor = clamp(fshade * baseCol, 0.0, 1.0);
        vec3 flatCol = texColor.rgb * matColor;
        if (sphereMode == 1) { flatCol *= sphTex.rgb; }
        fragColor = vec4(flatCol, alpha);
        return;
    }
    vec3 norm = normalize(vNormal);
    vec3 lightDir = normalize(lightPos - vFragPos);
    float hl = dot(norm, lightDir) * 0.5 + 0.5;
    vec3 toonCol = vec3(1.0);
    if (hasToon == 1) { toonCol = texture(toonSampler, vec2(0.5, 1.0 - hl)).rgb; }
    vec3 diffuse = objColor.rgb * lightColor * hl;
    vec3 lit = texColor.rgb * (diffuse + matAmbient) * toonCol;
    vec3 viewDir = normalize(viewPos - vFragPos);
    vec3 reflDir = reflect(-lightDir, norm);
    float spec = pow(max(dot(reflDir, viewDir), 0.0), max(shininess, 1.0));
    lit += spec * specColor * lightColor;
    vec3 sphMod = sphTex.rgb;
    if (sphereMode == 1) { lit *= sphMod; }
    else if (sphereMode == 2) { lit += sphMod; }
    else if (sphereMode == 3) { lit = mix(lit, sphTex.rgb, sphTex.a); }
    fragColor = vec4(clamp(lit, 0.0, 1.0), alpha);
}";
        public Form1()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 注册编码器
            InitializeComponent();
            MinimumSize = Size;
            toolTip1.SetToolTip(checkBox1, "由于作者不懂3D模型的渲染，这里的打光对于特定模型的渲染效果非常差，请大佬修改本程序的源代码。作者在此提前感谢。");
            toolTip1.SetToolTip(checkBox2, "本程序的操作方法改为MMD的操作风格，取消后恢复正常。");
            toolTip1.SetToolTip(checkBox3, "勾选该选项后，本程序可以显示检测到的本程序已知的模型自身存在的缺陷并将检测结果保存到程序的运行路径。\n本程序不保证能检测出模型的所有问题，敬请留意。");
            toolTip1.SetToolTip(checkBox4, "模拟MMD的物理引擎。作者对于复刻物理引擎能力有限，请大佬修改本程序的源代码。作者在此提前感谢。");
            if (DateTime.Now.ToString("MM-dd") == "12-23") { Text = "MMDViewer by：蔴球视频 Happy Birthday, Amiya!"; }
            Application.Idle += application_idle; // 播放帧由主循环空闲时驱动
        }
        private void load_model_file(string path)
        {
            try
            {
                model = PMXParser.Parse(path);
                file_path = path;
            }
            catch (FormatException ex) when (ex.Message == "Invalid magic word") // 尝试解密（该方法来自贴吧用户@0Shaya0，本人在此做出感谢！）
            {
                byte[] data = File.ReadAllBytes(path);
                data[3] = 0x20;
                using (MemoryStream ms = new MemoryStream(data))
                {
                    model = PMXParser.Parse(ms);
                    file_path = path;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解析MMD模型时出错，原因：{ex.Message}\n请把上述的错误原因报告给开发者以解决对应问题。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
#if !DEBUG // 一般情况下应该能看到模型信息
            if (!String.IsNullOrEmpty(model.Comment)) { MessageBox.Show(model.Comment, "模型信息", MessageBoxButtons.OK, MessageBoxIcon.Information); }
#endif
            label1.Text = $"加载的模型：{model.Name}";
            current_motion = null;
            camera_motion = null;
            bone_frame_map = new Dictionary<string, List<bone_keyframe>>();
            movable_bones = new HashSet<string>();
            anim_playing = false;
            anim_paused = false;
            current_frame = 0;
            anim_frame_offset = 0f;
            display_rest_pose = true;
            phys_needs_reset = true;
            setup_model();
            update_playback_buttons();
            glControl1.Invalidate();
            button5.Enabled = true;
            checkBox3.Enabled = true;
        }
        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            if (anim_playing || anim_paused) { return; }
            string[] files = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
            if (files.Length == 0) { return; }
            string path = files[0];
            if (!Path.GetExtension(path).Equals(".pmx", StringComparison.OrdinalIgnoreCase)) { return; }
            load_model_file(path);
        }
        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (anim_playing || anim_paused)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }
        private void Form1_FormClosed(object? sender, FormClosedEventArgs e)
        {
            Application.Idle -= application_idle;
            destroy_physics();
            if (vao != 0 || shader_program != 0)
            {
                glControl1.MakeCurrent();
                delete_model_resources();
                if (default_texture != 0)
                {
                    GL.DeleteTexture(default_texture);
                    default_texture = 0;
                }
                if (default_toon_texture != 0)
                {
                    GL.DeleteTexture(default_toon_texture);
                    default_toon_texture = 0;
                }
                if (shader_program != 0)
                {
                    GL.DeleteProgram(shader_program);
                    shader_program = 0;
                }
            }
        }
        private void button1_Click(object? sender, EventArgs e)
        {
            camera_target = new OV3(0, view_target_y, 0);
            camera_rotation = Quaternion.Identity;
            camera_distance = model_size * 2.2f;
            glControl1.Invalidate();
        }
        private void button2_Click(object? sender, EventArgs e)
        {
            camera_target = new OV3(0, view_target_y, 0);
            camera_rotation = Quaternion.Identity;
            camera_distance = model_size * 2.2f;
            glControl1.Invalidate();
        }
        private void button3_Click(object? sender, EventArgs e)
        {
            camera_target = new OV3(0, view_target_y, 0);
            camera_rotation = Quaternion.FromAxisAngle(OV3.UnitX, -OMH.PiOver2);
            camera_distance = model_size * 2.2f;
            glControl1.Invalidate();
        }
        private void button4_Click(object? sender, EventArgs e)
        {
            camera_target = new OV3(0, view_target_y, 0);
            camera_rotation = Quaternion.FromAxisAngle(OV3.UnitY, -OMH.PiOver2);
            camera_distance = model_size * 2.2f;
            glControl1.Invalidate();
        }
        private void button5_Click(object? sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请问您是怎么点击这个按钮的？", "", MessageBoxButtons.OK, MessageBoxIcon.Question);
                return;
            }
            openFileDialog1.Filter = "VMD动作文件|*.vmd";
            openFileDialog1.Title = "选择VMD动作文件";
            if (openFileDialog1.ShowDialog() != DialogResult.OK) { return; }
            vmd_motion parsed;
            try { parsed = vmd_parser.parse(openFileDialog1.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show($"解析vmd动作时出错，原因：{ex.Message}\n请把上述的错误原因报告给开发者以解决对应问题。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (parsed.bone_frames.Count == 0 && parsed.morph_frames.Count == 0)
            {
                MessageBox.Show("该文件不含动作数据，你可能选错了文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            current_motion = parsed;
            label1.Text = $"加载的模型：{model!.Name} | 动作：{current_motion.model_name}";
            setup_animation();
            anim_playing = false;
            anim_paused = false;
            current_frame = 0;
            compute_bone_transforms();
            display_rest_pose = false; // 导入后展示动作第0帧姿势
            update_playback_buttons();
            glControl1.Invalidate();
        }
        private void button6_Click(object? sender, EventArgs e)
        {
            if (current_motion == null && camera_motion == null) { return; }
            current_frame = 0;
            anim_frame_offset = 0f;
            phys_needs_reset = true; // 从头播放，刚体传送回起始姿势
            display_rest_pose = false;
            anim_playing = true;
            anim_paused = false;
            anim_clock.Restart();
            frame_timer.Restart();
            advance_timer.Restart();
            update_playback_buttons();
            glControl1.Invalidate();
        }
        private void button7_Click(object? sender, EventArgs e)
        {
            if (anim_playing)
            {
                anim_paused = true;
                anim_playing = false;
                update_playback_buttons();
                update_playback_label();
            }
            else if (anim_paused)
            {
                anim_frame_offset = current_frame;
                anim_paused = false;
                anim_playing = true;
                anim_clock.Restart();
                frame_timer.Restart();
                advance_timer.Restart();
                update_playback_buttons();
                glControl1.Invalidate();
            }
        }
        private void button8_Click(object? sender, EventArgs e)
        {
            anim_playing = false;
            anim_paused = false;
            current_frame = 0;
            reset_to_rest_pose();
            display_rest_pose = true;  // 停止后静止姿势回到rest pose
            phys_needs_reset = true;   // 停止后骨骼回到rest pose，下次播放前刚体需要重新就位
            if (camera_motion != null) // 停止后交还相机手动控制，恢复默认视角与FOV
            {
                camera_fov = 30f;
                camera_target = new OV3(0, view_target_y, 0);
                camera_rotation = Quaternion.Identity;
                camera_distance = model_size * 2.2f;
            }
            update_playback_buttons();
            glControl1.Invalidate();
            label3.Text = "已停止播放";
        }
        private void button9_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                MessageBox.Show("请问您是怎么点击这个按钮的？", "", MessageBoxButtons.OK, MessageBoxIcon.Question);
                return;
            }
            openFileDialog1.Filter = "VMD镜头文件|*.vmd";
            openFileDialog1.Title = "选择VMD镜头文件";
            if (openFileDialog1.ShowDialog() != DialogResult.OK) { return; }
            try { camera_motion = vmd_parser.parse(openFileDialog1.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show($"解析vmd镜头时出错，原因：{ex.Message}\n请把上述的错误原因报告给开发者以解决对应问题。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (camera_motion.camera_frames.Count == 0)
            {
                MessageBox.Show("该文件不包含镜头数据，你可能选错了文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            evaluate_camera(0f); // 立即应用第一帧镜头，便于预览取景
            update_playback_buttons();
            glControl1.Invalidate();
        }
        private void button12_Click(object sender, EventArgs e)
        {
            if (current_motion == null && camera_motion == null) { return; }
            bool had_camera = camera_motion != null;
            // 卸载动作与镜头数据（morph顶点/材质状态由update_morphs在current_motion为null时自动回退）
            current_motion = null;
            camera_motion = null;
            bone_frame_map = new Dictionary<string, List<bone_keyframe>>();
            movable_bones = new HashSet<string>();
            anim_playing = false;
            anim_paused = false;
            current_frame = 0;
            anim_frame_offset = 0f;
            reset_to_rest_pose();
            display_rest_pose = true;
            phys_needs_reset = true;  // 骨骼回到rest pose，下次播放前刚体需要重新就位
            if (had_camera) // 镜头曾接管相机，清除后交还手动控制，恢复默认视角与FOV
            {
                camera_fov = 30f;
                camera_target = new OV3(0, view_target_y, 0);
                camera_rotation = Quaternion.Identity;
                camera_distance = model_size * 2.2f;
            }
            if (model != null) { label1.Text = $"加载的模型：{model.Name}"; }
            update_playback_buttons();
            glControl1.Invalidate();
        }
        private void update_playback_buttons()
        {
            bool loaded = current_motion != null || camera_motion != null;
            bool active = anim_playing || anim_paused;
            button5.Enabled = model != null && !active;  // 加载动作
            button9.Enabled = model != null && !active;  // 加载镜头
            button6.Enabled = loaded && !active;         // 播放
            button7.Enabled = loaded && active;          // 暂停/继续
            button8.Enabled = loaded && active;          // 停止
            button12.Enabled = loaded;                   // 清除VMD
            button7.Text = anim_paused ? "继续" : "暂停";
        }
        private void button10_Click(object sender, EventArgs e)
        {
            MessageBox.Show("MMDViewer\n蔴球视频 2026.07.17\n本程序基于MIT许可证发布，您可以自由使用、修改和分发本程序。\n本程序的实现参考了saba、babylon-mmd、MikuMikuFlex以及three.js，作者在此表达感谢。\n本程序的开源仓库链接为：https://github.com/MQVideo/MMDViewer", "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void button11_Click(object sender, EventArgs e)
        {
            if (model == null)
            {
                if (MessageBox.Show("你确定要在没加载模型的情况下截图吗？", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) { return; }
            }
            string base_name = Path.GetFileNameWithoutExtension(file_path);
            if (base_name.Length == 0) { base_name = "screenshot"; }
            saveFileDialog1.Filter = "PNG图片|*.png|JPEG图片|*.jpg|BMP图片|*.bmp";
            saveFileDialog1.Title = "保存截图";
            saveFileDialog1.FileName = $"{base_name}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            if (saveFileDialog1.ShowDialog() != DialogResult.OK) { return; }
            try
            {
                save_screenshot(saveFileDialog1.FileName);
                if (!checkBox3.Checked) { label3.Text = $"截图已保存：{Path.GetFileName(saveFileDialog1.FileName)}"; } // 自检模式下label3保留自检结果
            }
            catch (Exception ex) { MessageBox.Show($"保存截图时出错，原因：{ex.Message}\n请把上述的错误原因报告给开发者以解决对应问题。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void checkBox1_CheckedChanged(object? sender, EventArgs e)
        {
            lighting_enabled = checkBox1.Checked;
            glControl1.Invalidate();
        }
        private void checkBox2_CheckedChanged(object? sender, EventArgs e)
        {
            if (checkBox2.Checked) { label2.Text = "操作指南：\r\n右键拖拽旋转，滚轮拖拽平移，滚轮缩放。"; }
            else { label2.Text = "操作指南：\r\n左键拖拽旋转，右键拖拽平移，滚轮缩放。"; }
        }
        private void checkBox3_CheckedChanged(object? sender, EventArgs e)
        {
            string ori_label = label3.Text;
            if (model == null)
            {
                MessageBox.Show("请问您是怎么触发这个状态的？", "", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }
            if (checkBox3.Checked) { run_self_check(); }
            else
            {
                if (current_motion != null) { update_playback_label(); }
                else
                {
                    label3.Text = ori_label;
                    glControl1.Invalidate();
                }
            }
        }
        private void run_self_check()
        {
            build_model_diagnostics(); // 确保model_issues为最新
            StringBuilder sb = new StringBuilder();
            string model_name = Path.GetFileNameWithoutExtension(file_path);
            if (model_issues.Count == 0) { label3.Text = "模型未检测到本程序已知的异常。"; }
            else
            {
                sb.AppendLine($"检测到{model_issues.Count}项异常：");
                for (int i = 0; i < model_issues.Count; i++) { sb.AppendLine($"{i + 1}. {model_issues[i]}"); }
                dump_material_draw_order(sb);
                string diag_name = $"{model_name}_diag.txt";
                string diag_path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, diag_name);
                File.WriteAllText(diag_path, sb.ToString().TrimEnd(), Encoding.UTF8);
                label3.Text = $"本程序已检测到模型异常，已写入文件到程序路径。";
            }
        }
        private void dump_material_draw_order(StringBuilder sb) // 转储材质的实际绘制先后
        {
            if (model == null) { return; }
            ReadOnlySpan<Material> mats = model.MaterialList.Span;
            sb.AppendLine("材质绘制顺序（按实际绘制先后）");
            int order = 1;
            sb.AppendLine("不透明材质（Alpha>=0.5）：");
            int opaque_start = order;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i].Diffuse.A < 0.5f) { continue; }
                sb.AppendLine($"{order}. 材质#{i} {mats[i].Name}  (Alpha={mats[i].Diffuse.A:F2})");
                order++;
            }
            if (order == opaque_start) { sb.AppendLine("无"); }
            sb.AppendLine("透明材质（Alpha<0.5）：");
            int transparent_start = order;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i].Diffuse.A >= 0.5f) { continue; }
                sb.AppendLine($"{order}. 材质#{i} {mats[i].Name}  (Alpha={mats[i].Diffuse.A:F2})");
                order++;
            }
            if (order == transparent_start) { sb.AppendLine("无"); }
        }
        private static string join_names(List<string> names)
        {
            if (names.Count == 0) { return ""; }
            if (names.Count <= 3) { return "（" + string.Join("、", names) + "）"; }
            return string.Join("\n", names);
        }
        private void glControl1_Load(object? sender, EventArgs e)
        {
            glControl1.MakeCurrent();
            shader_program = compile_program(vertex_shader, frag_shader);
            cache_uniform_locations();
            default_texture = create_white_texture();
            default_toon_texture = create_default_toon_texture();
            GL.Enable(EnableCap.DepthTest);
            glControl1_Resize(null, EventArgs.Empty);
        }
        private void glControl1_Resize(object? sender, EventArgs e)
        {
            if (glControl1.ClientSize.Height == 0) { return; }
            glControl1.MakeCurrent();
            GL.Viewport(0, 0, glControl1.ClientSize.Width, glControl1.ClientSize.Height);
            update_projection();
        }
        private void update_projection()
        {
            if (glControl1.ClientSize.Height == 0) { return; }
            float aspect = (float)glControl1.ClientSize.Width / Math.Max(1, glControl1.ClientSize.Height);
            projection = build_projection(aspect);
        }
        private OM4 build_projection(float aspect) // 投影矩阵构建
        {
            float abs_dist = MathF.Abs(camera_distance); // 镜头VMD的distance取反后仍可能为负（相机穿到目标前方），裁面用绝对值
            float near_plane = MathF.Max(abs_dist * 0.05f, 0.02f);
            float far_plane = abs_dist + model_size * 4f + 10f;
            float fov = OMH.Clamp(camera_fov, 1f, 175f); // 镜头数据异常FOV防护
            return OM4.CreatePerspectiveFieldOfView(OMH.DegreesToRadians(fov), aspect, near_plane, far_plane);
        }
        private void glControl1_Paint(object? sender, PaintEventArgs e)
        {
            render();
        }
        private void glControl1_MouseDown(object? sender, MouseEventArgs e)
        {
            last_mouse_pos = e.Location;
            if (e.Button == MouseButtons.Left) { mouse_left_down = true; }
            if (e.Button == MouseButtons.Right) { mouse_right_down = true; }
            if (e.Button == MouseButtons.Middle) { mouse_middle_down = true; }
        }
        private void glControl1_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { mouse_left_down = false; }
            if (e.Button == MouseButtons.Right) { mouse_right_down = false; }
            if (e.Button == MouseButtons.Middle) { mouse_middle_down = false; }
        }
        private void glControl1_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!mouse_left_down && !mouse_right_down && !mouse_middle_down) { return; }
            float dx = e.X - last_mouse_pos.X;
            float dy = e.Y - last_mouse_pos.Y;
            last_mouse_pos = e.Location;
            if (checkBox2.Checked)
            {
                if (mouse_right_down)
                {
                    float sensitivity = 0.005f;
                    Quaternion rot_h = Quaternion.FromAxisAngle(OV3.UnitY, dx * sensitivity);
                    OV3 right = OV3.Transform(OV3.UnitX, camera_rotation);
                    Quaternion rot_v = Quaternion.FromAxisAngle(right, -dy * sensitivity);
                    camera_rotation = Quaternion.Normalize(rot_v * rot_h * camera_rotation);
                }
                if (mouse_middle_down)
                {
                    OV3 right = OV3.Transform(OV3.UnitX, camera_rotation);
                    OV3 up = OV3.Transform(OV3.UnitY, camera_rotation);
                    float pan_speed = camera_distance * 0.001f;
                    camera_target += right * dx * pan_speed + up * dy * pan_speed;
                }
            }
            else
            {
                if (mouse_left_down)
                {
                    float sensitivity = 0.005f;
                    Quaternion rot_h = Quaternion.FromAxisAngle(OV3.UnitY, dx * sensitivity);
                    OV3 right = OV3.Transform(OV3.UnitX, camera_rotation);
                    Quaternion rot_v = Quaternion.FromAxisAngle(right, -dy * sensitivity);
                    camera_rotation = Quaternion.Normalize(rot_v * rot_h * camera_rotation);
                }
                if (mouse_right_down)
                {
                    OV3 right = OV3.Transform(OV3.UnitX, camera_rotation);
                    OV3 up = OV3.Transform(OV3.UnitY, camera_rotation);
                    float pan_speed = camera_distance * 0.001f;
                    camera_target += right * dx * pan_speed + up * dy * pan_speed;
                }
            }
            if (model != null) { glControl1.Invalidate(); }
        }
        private void glControl1_MouseWheel(object? sender, MouseEventArgs e)
        {
            camera_distance *= e.Delta > 0 ? 0.9f : 1.1f; // 滚轮每格缩放10%
            // camera_distance = OMH.Clamp(camera_distance, model_size * 0.1f, model_size * 100f); // 缩放范围为模型尺寸的10%~100倍
            if (model != null) { glControl1.Invalidate(); }
        }
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo link = new ProcessStartInfo
            {
                FileName = "https://space.bilibili.com/79427854",
                UseShellExecute = true
            };
            Process.Start(link);
        }
        private void setup_model()
        {
            if (model == null) { return; }
            glControl1.MakeCurrent();
            delete_model_resources();
            GL.ActiveTexture(TextureUnit.Texture0);
            ReadOnlySpan<Vertex> verts = model.VertexList.Span;
            ReadOnlySpan<Surface> surfs = model.SurfaceList.Span;
            ReadOnlySpan<Material> mats = model.MaterialList.Span;
            build_draw_groups(mats);
            compute_bounding_box(verts, surfs);
            glControl1_Resize(null, EventArgs.Empty);
            camera_target = new OV3(0, view_target_y, 0);
            camera_rotation = Quaternion.Identity;
            load_textures();
            float[] pos = new float[verts.Length * 3];
            float[] nrm = new float[verts.Length * 3];
            float[] uv = new float[verts.Length * 2];
            int[] idx = new int[surfs.Length * 3];
            for (int i = 0; i < verts.Length; i++)
            {
                Vertex v = verts[i];
                // PMX是右手坐标系Z轴朝上，OpenGL右手坐标系Y轴朝上，翻转Z
                pos[i * 3 + 0] = v.Position.X;
                pos[i * 3 + 1] = v.Position.Y;
                pos[i * 3 + 2] = -v.Position.Z;
                nrm[i * 3 + 0] = v.Normal.X;
                nrm[i * 3 + 1] = v.Normal.Y;
                nrm[i * 3 + 2] = -v.Normal.Z;
                uv[i * 2 + 0] = v.UV.X;
                uv[i * 2 + 1] = v.UV.Y;
            }
            for (int i = 0; i < surfs.Length; i++)
            {
                Surface s = surfs[i];
                // PMX顶点顺序V1-V2-V3（顺时针）转OpenGL V1-V3-V2（逆时针），配合Z翻转修正绕序
                idx[i * 3 + 0] = s.V1;
                idx[i * 3 + 1] = s.V3;
                idx[i * 3 + 2] = s.V2;
            }
            // 应用基础morph修正（Neutral等），解决部分模型面部mesh基态不正确的问题
            apply_base_morphs(pos, uv, model.MorphList.Span);
            // 保存施加base morph后的基态副本，供运行时VMD表情morph在其上叠加
            morph_base_pos = (float[])pos.Clone();
            morph_base_uv = (float[])uv.Clone();
            morph_work_pos = new float[pos.Length];
            morph_work_uv = new float[uv.Length];
            build_morph_index();
            morph_applied = false;
            last_morph_frame = -1f;
            // 构建骨骼索引和权重数组用于GPU蒙皮（Texture Buffer无骨骼数上限，仅排除无效索引）
            int bone_cnt = model.BoneList.Span.Length;
            int[] bone_idx_arr = new int[verts.Length * 4];
            float[] bone_wt_arr = new float[verts.Length * 4];
            for (int i = 0; i < verts.Length; i++)
            {
                Vertex v = verts[i];
                int j = i * 4;
                if (v.WeightTransformType == WeightTransformType.BDEF1) { pack_bone_weight(j, v.BoneIndex1, 1f, bone_cnt, bone_idx_arr, bone_wt_arr); }
                else
                {
                    float w1 = v.Weight1;
                    float w2 = (v.WeightTransformType == WeightTransformType.BDEF4 || v.WeightTransformType == WeightTransformType.QDEF) ? v.Weight2 : 1f - w1;
                    float w3 = (v.WeightTransformType == WeightTransformType.BDEF4 || v.WeightTransformType == WeightTransformType.QDEF) ? v.Weight3 : 0f;
                    float w4 = (v.WeightTransformType == WeightTransformType.BDEF4 || v.WeightTransformType == WeightTransformType.QDEF) ? v.Weight4 : 0f;
                    pack_bone_weight(j + 0, v.BoneIndex1, w1, bone_cnt, bone_idx_arr, bone_wt_arr);
                    pack_bone_weight(j + 1, v.BoneIndex2, w2, bone_cnt, bone_idx_arr, bone_wt_arr);
                    pack_bone_weight(j + 2, v.BoneIndex3, w3, bone_cnt, bone_idx_arr, bone_wt_arr);
                    pack_bone_weight(j + 3, v.BoneIndex4, w4, bone_cnt, bone_idx_arr, bone_wt_arr);
                }
            }
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(int), idx, BufferUsageHint.StaticDraw);
            position_vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, position_vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, pos.Length * sizeof(float), pos, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
            normal_vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, normal_vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, nrm.Length * sizeof(float), nrm, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 0, 0);
            uv_vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, uv_vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, uv.Length * sizeof(float), uv, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 0, 0);
            bone_index_vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, bone_index_vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bone_idx_arr.Length * sizeof(int), bone_idx_arr, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribIPointer(3, 4, VertexAttribIntegerType.Int, 0, IntPtr.Zero);
            bone_weight_vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, bone_weight_vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, bone_wt_arr.Length * sizeof(float), bone_wt_arr, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, 0, 0);
            // 创建骨骼缓冲纹理（Texture Buffer）并初始化为Identity，每根骨骼占4个RGBA32F texel
            int tb = Math.Max(1, bone_cnt);
            bone_tex_capacity = tb;
            bone_tex_buf = new OM4[tb];
            for (int bi = 0; bi < tb; bi++) { bone_tex_buf[bi] = OM4.Identity; }
            bone_buffer = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.TextureBuffer, bone_buffer);
            GL.BufferData(BufferTarget.TextureBuffer, bone_tex_buf.Length * 16 * sizeof(float), bone_tex_buf, BufferUsageHint.DynamicDraw);
            bone_texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.TextureBuffer, bone_texture);
            GL.TexBuffer(TextureBufferTarget.TextureBuffer, SizedInternalFormat.Rgba32f, bone_buffer);
            GL.BindTexture(TextureTarget.TextureBuffer, 0);
            GL.BindBuffer(BufferTarget.TextureBuffer, 0);
            GL.BindVertexArray(0);
            setup_bone_data(); // 骨骼层级/IK/变形顺序在加载模型时就绪
            create_physics();
            build_model_diagnostics();
            if (checkBox3.Checked) { run_self_check(); }
        }
        private void build_model_diagnostics()
        {
            model_issues.Clear();
            if (bad_verts > 0) { model_issues.Add($"{bad_verts}个坏顶点（NaN/Inf，已忽略）"); }
            if (outlier_verts > 0) { model_issues.Add($"{outlier_verts}个离群顶点，已自动修正取景"); }
            if (isolated_verts > 0) { model_issues.Add($"{isolated_verts}个孤立顶点（未被面引用，不渲染，已从取景中剔除）"); }
            if (missing_tex > 0) { model_issues.Add($"{missing_tex}个纹理缺失（文件不存在）：\n{join_names(missing_tex_names)}"); }
            if (failed_tex > 0) { model_issues.Add($"{failed_tex}个纹理加载失败（格式不支持或文件损坏）：\n{join_names(failed_tex_names)}"); }
            if (disabled_render_tex > 0)
            {
                string suffix = disabled_render_tex_names.Count > 0 ? "：" + string.Join("、", disabled_render_tex_names) : "";
                model_issues.Add($"{disabled_render_tex}个禁用渲染的纹理（材质将其渲染模式设为禁用、故不显示，属模型无效引用而非程序错误）{suffix}");
            }
            if (unref_tex > 0) { model_issues.Add($"{unref_tex}个未被材质引用的纹理（不参与渲染。常见于Vroid导出的法线贴图/matcap等本程序不支持的高级贴图）{join_names(unref_tex_names)}"); }
            if (model_issues.Count == 0) { model_issue_summary = ""; }
            else { model_issue_summary = "检测到模型有以下问题：" + string.Join("；", model_issues); }
        }
        private void build_draw_groups(ReadOnlySpan<Material> materials)
        {
            draw_groups.Clear();
            int start = 0;
            foreach (Material mat in materials)
            {
                int faces = mat.VertexCount / 3;
                // first_index是byte偏移（start * 3 个顶点 * sizeof(int)），count是顶点数
                draw_groups.Add((start * 3 * sizeof(int), faces * 3, mat));
                start += faces;
            }
        }
        private static void pack_bone_weight(int out_offset, int raw_index, float weight, int bone_cnt, int[] idx_arr, float[] wt_arr)
        {
            // 骨骼索引为-1（PMX中表示无骨骼）或超出模型骨骼总数权重清零，索引设为0（着色器中有 idx >= 0 检查）
            int idx = raw_index;
            if (idx < 0 || idx >= bone_cnt) 
            {
                idx = 0; 
                weight = 0f; 
            }
            idx_arr[out_offset] = idx;
            wt_arr[out_offset] = weight;
        }
        private void compute_bounding_box(ReadOnlySpan<Vertex> verts, ReadOnlySpan<Surface> surfs)
        {
            bad_verts = 0;
            outlier_verts = 0;
            isolated_verts = 0;
            if (verts.Length == 0) 
            {
                model_center = OV3.Zero;
                model_size = 1f; 
                camera_distance = 2.2f;
                return; 
            }
            bool[] used = new bool[verts.Length];
            int used_count = 0;
            foreach (Surface s in surfs)
            {
                int v1 = s.V1, v2 = s.V2, v3 = s.V3;
                if (v1 >= 0 && v1 < verts.Length && !used[v1]) 
                { 
                    used[v1] = true; 
                    used_count++; 
                }
                if (v2 >= 0 && v2 < verts.Length && !used[v2])
                { 
                    used[v2] = true;
                    used_count++;
                }
                if (v3 >= 0 && v3 < verts.Length && !used[v3]) 
                { 
                    used[v3] = true;
                    used_count++; 
                }
            }
            // 无面数据（异常模型）时回退到全部顶点，避免把包围盒算空
            bool use_face_filter = used_count > 0;
            if (use_face_filter) { isolated_verts = verts.Length - used_count; }
            // 第一遍收集有限顶点各轴坐标，用中位数估计稳健中心（对离群点/NaN不敏感）
            float[] xs = new float[verts.Length];
            float[] ys = new float[verts.Length];
            float[] zs = new float[verts.Length];
            int m = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                if (use_face_filter && !used[i]) { continue; }
                Vertex v = verts[i];
                if (!float.IsFinite(v.Position.X) || !float.IsFinite(v.Position.Y) || !float.IsFinite(v.Position.Z))
                {
                    bad_verts++;
                    continue;
                }
                xs[m] = v.Position.X;
                ys[m] = v.Position.Y;
                zs[m] = v.Position.Z;
                m++;
            }
            if (m == 0) // 全部顶点都无效时的兜底
            { 
                model_center = OV3.Zero;
                model_size = 1f;
                camera_distance = 2.2f; 
                return; 
            }
            Array.Sort(xs, 0, m);
            Array.Sort(ys, 0, m); 
            Array.Sort(zs, 0, m);
            float cx = xs[m / 2], cy = ys[m / 2], cz = zs[m / 2]; // 中位数中心
            // 到中位数中心的距离中位数，作为本体尺度的稳健估计
            float[] dist = new float[m];
            int di = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                if (use_face_filter && !used[i]) { continue; }
                Vertex v = verts[i];
                if (!float.IsFinite(v.Position.X) || !float.IsFinite(v.Position.Y) || !float.IsFinite(v.Position.Z)) { continue; }
                float dx = v.Position.X - cx, dy = v.Position.Y - cy, dz = v.Position.Z - cz;
                dist[di] = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                di++;
            }
            Array.Sort(dist);
            float median_dist = dist[m / 2];
            const float outlier_k = 20f; // 稳健取景阈值
            float threshold = median_dist * outlier_k;
            if (median_dist < 1e-4f) { threshold = float.MaxValue; } // 退化情形（顶点全重合）不裁剪
            float overall_spread = Math.Max(xs[m - 1] - xs[0], Math.Max(ys[m - 1] - ys[0], zs[m - 1] - zs[0]));
            bool degenerate_pile = overall_spread > 1e-3f && median_dist < overall_spread * 1e-3f && median_dist < 0.1f;
            // 第二遍收集用于求包围盒的顶点
            float[] ix = new float[m];
            float[] iy = new float[m];
            float[] iz = new float[m];
            int n_in = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                if (use_face_filter && !used[i]) { continue; }
                Vertex v = verts[i];
                if (!float.IsFinite(v.Position.X) || !float.IsFinite(v.Position.Y) || !float.IsFinite(v.Position.Z)) { continue; }
                float dx = v.Position.X - cx, dy = v.Position.Y - cy, dz = v.Position.Z - cz;
                bool inside = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) <= threshold;
                if (degenerate_pile) // 剔除点堆本身
                {
                    if (inside)
                    {
                        outlier_verts++;
                        continue;
                    }
                }
                else // 剔除飞远坏几何
                {
                    if (!inside)
                    {
                        outlier_verts++;
                        continue;
                    }
                }
                ix[n_in] = v.Position.X;
                iy[n_in] = v.Position.Y;
                iz[n_in] = v.Position.Z;
                n_in++;
            }
            if (n_in == 0) // 兜底
            {
                model_center = OV3.Zero;
                model_size = 1f;
                camera_distance = 2.2f;
                return;
            }
            Array.Sort(ix, 0, n_in);
            Array.Sort(iy, 0, n_in);
            Array.Sort(iz, 0, n_in);
            int lo, hi;
            if (degenerate_pile)
            {
                // 中位数中心落在点堆里不代表本体，从本体顶点重算中位数中心，使相机注视本体而非点堆
                cx = ix[n_in / 2]; cy = iy[n_in / 2]; cz = iz[n_in / 2];
                lo = n_in / 100;            // 1%分位下标
                hi = n_in - 1 - n_in / 100; // 99%分位下标
            }
            else
            {
                lo = n_in / 200;            // 0.5%分位下标
                hi = n_in - 1 - n_in / 200; // 99.5%分位下标
            }
            if (hi <= lo) { lo = 0; hi = n_in - 1; } // 顶点太少时不裁剪
            float min_x = ix[lo], max_x = ix[hi];
            float min_y = iy[lo], max_y = iy[hi];
            float min_z = iz[lo], max_z = iz[hi];
            model_center = new OV3(cx, cy, -cz);
            model_size = Math.Max(max_x - min_x, Math.Max(max_y - min_y, max_z - min_z));
            if (model_size < 0.001f) { model_size = 1f; } // 模型极小或为空时赋予默认大小，避免相机计算异常
            float raw_target_y = (min_y + max_y) / 2f - cy;
            float max_off = model_size * 0.45f;
            view_target_y = OMH.Clamp(raw_target_y, -max_off, max_off);
            camera_distance = model_size * 2.2f; // 相机默认距离，使模型填充画面约85%
        }
        private static void apply_base_morphs(float[] pos, float[] uv, ReadOnlySpan<Morph> morphs)
        {
            foreach (Morph m in morphs)
            {
                string nl = m.Name.ToLower();
                if (!nl.Contains("neutral") && !nl.Contains("base") && !nl.Contains("通常") && !nl.Contains("標準"))
                { continue; }
                if (m.MorphType == MorphType.Vertex)
                {
                    ReadOnlySpan<VertexMorphElement> elements = m.VertexMorphElements.Span;
                    for (int i = 0; i < elements.Length; i++)
                    {
                        VertexMorphElement e = elements[i];
                        int vi = e.TargetVertex * 3;
                        if (vi + 2 >= pos.Length) { continue; }
                        pos[vi + 0] += e.PosOffset.X;
                        pos[vi + 1] += e.PosOffset.Y;
                        pos[vi + 2] += -e.PosOffset.Z; // PMX转OpenGL Z翻转
                    }
                }
                else if (m.MorphType == MorphType.UV)
                {
                    ReadOnlySpan<UVMorphElement> elements = m.UVMorphElements.Span;
                    for (int i = 0; i < elements.Length; i++)
                    {
                        UVMorphElement e = elements[i];
                        int vi = e.TargetVertex * 2;
                        if (vi + 1 >= uv.Length) { continue; }
                        uv[vi + 0] += e.UVOffset.X;
                        uv[vi + 1] += e.UVOffset.Y;
                    }
                }
            }
        }
        private void build_morph_index() // 构建morph名-索引的查找表
        {
            morph_name_index.Clear();
            has_morphs = false;
            has_bone_morph = false;
            has_material_morph = false;
            if (model == null) { return; }
            ReadOnlySpan<Morph> morphs = model.MorphList.Span;
            has_morphs = morphs.Length > 0;
            for (int i = 0; i < morphs.Length; i++)
            {
                string nm = morphs[i].Name;
                if (!string.IsNullOrEmpty(nm) && !morph_name_index.ContainsKey(nm)) { morph_name_index[nm] = i; }
                string en = morphs[i].NameEnglish;
                if (!string.IsNullOrEmpty(en) && !morph_name_index.ContainsKey(en)) { morph_name_index[en] = i; }
                if (morphs[i].MorphType == MorphType.Bone) { has_bone_morph = true; }
                else if (morphs[i].MorphType == MorphType.Material) { has_material_morph = true; }
            }
            // 组合morph可能间接引用骨骼/材质子morph，上面按类型扫描已覆盖(子morph本身也在列表中)
            int bone_cnt = model.BoneList.Span.Length;
            bone_morph_trans = new OV3[bone_cnt];
            bone_morph_rot = new OQ[bone_cnt];
            int mat_cnt = model.MaterialList.Span.Length;
            matm_diffuse_mul = new OV4[mat_cnt];
            matm_diffuse_add = new OV4[mat_cnt];
            matm_specular_mul = new OV3[mat_cnt];
            matm_specular_add = new OV3[mat_cnt];
            matm_shininess_mul = new float[mat_cnt];
            matm_shininess_add = new float[mat_cnt];
            matm_ambient_mul = new OV3[mat_cnt];
            matm_ambient_add = new OV3[mat_cnt];
            matm_active = false;
        }
        private void update_morphs()
        {
            if (!has_morphs || morph_base_pos.Length == 0) { return; }
            if (current_motion == null || current_motion.morph_frame_map.Count == 0 || (!anim_playing && !anim_paused))
            {
                if (morph_applied)
                {
                    GL.BindBuffer(BufferTarget.ArrayBuffer, position_vbo);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, morph_base_pos.Length * sizeof(float), morph_base_pos);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, uv_vbo);
                    GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, morph_base_uv.Length * sizeof(float), morph_base_uv);
                    morph_applied = false;
                    last_morph_frame = -1f;
                }
                return;
            }
            if (current_frame == last_morph_frame && morph_applied) { return; } // 帧未变，避免重复计算与上传
            last_morph_frame = current_frame;
            Array.Copy(morph_base_pos, morph_work_pos, morph_base_pos.Length);
            Array.Copy(morph_base_uv, morph_work_uv, morph_base_uv.Length);
            ReadOnlySpan<Morph> morphs = model!.MorphList.Span;
            foreach (KeyValuePair<string, List<morph_keyframe>> kv in current_motion.morph_frame_map)
            {
                if (!morph_name_index.TryGetValue(kv.Key, out int mi)) { continue; }
                float w = interp_morph_weight(kv.Value, current_frame);
                if (MathF.Abs(w) < 1e-5f) { continue; }
                apply_morph(morphs, mi, w, 0);
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, position_vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, morph_work_pos.Length * sizeof(float), morph_work_pos);
            GL.BindBuffer(BufferTarget.ArrayBuffer, uv_vbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, morph_work_uv.Length * sizeof(float), morph_work_uv);
            morph_applied = true;
        }
        private void apply_morph(ReadOnlySpan<Morph> morphs, int mi, float weight, int depth)
        {
            if (mi < 0 || mi >= morphs.Length || depth > 8) { return; }
            Morph m = morphs[mi];
            if (m.MorphType == MorphType.Vertex)
            {
                ReadOnlySpan<VertexMorphElement> els = m.VertexMorphElements.Span;
                for (int i = 0; i < els.Length; i++)
                {
                    int vi = els[i].TargetVertex * 3;
                    if (vi < 0 || vi + 2 >= morph_work_pos.Length) { continue; }
                    morph_work_pos[vi + 0] += els[i].PosOffset.X * weight;
                    morph_work_pos[vi + 1] += els[i].PosOffset.Y * weight;
                    morph_work_pos[vi + 2] += -els[i].PosOffset.Z * weight; // PMX转OpenGL Z翻转
                }
            }
            else if (m.MorphType == MorphType.UV)
            {
                ReadOnlySpan<UVMorphElement> els = m.UVMorphElements.Span;
                for (int i = 0; i < els.Length; i++)
                {
                    int vi = els[i].TargetVertex * 2;
                    if (vi < 0 || vi + 1 >= morph_work_uv.Length) { continue; }
                    morph_work_uv[vi + 0] += els[i].UVOffset.X * weight;
                    morph_work_uv[vi + 1] += els[i].UVOffset.Y * weight;
                }
            }
            else if (m.MorphType == MorphType.Group)
            {
                ReadOnlySpan<GroupMorphElement> els = m.GroupMorphElements.Span;
                for (int i = 0; i < els.Length; i++) { apply_morph(morphs, els[i].TargetMorph, weight * els[i].MorphRatio, depth + 1); }
            }
        }
        private void apply_bone_morphs(int n)
        {
            if (!has_bone_morph || current_motion == null || current_motion.morph_frame_map.Count == 0
                || (!anim_playing && !anim_paused)) { return; }
            if (bone_morph_trans.Length < n) { return; }
            for (int i = 0; i < n; i++) { bone_morph_trans[i] = OV3.Zero; bone_morph_rot[i] = OQ.Identity; }
            ReadOnlySpan<Morph> morphs = model!.MorphList.Span;
            foreach (KeyValuePair<string, List<morph_keyframe>> kv in current_motion.morph_frame_map)
            {
                if (!morph_name_index.TryGetValue(kv.Key, out int mi)) { continue; }
                float w = interp_morph_weight(kv.Value, current_frame);
                if (MathF.Abs(w) < 1e-5f) { continue; }
                collect_bone_morph(morphs, mi, w, 0, n);
            }
            for (int i = 0; i < n; i++)
            {
                if (bone_morph_trans[i] != OV3.Zero) { bone_anim_offset[i] += bone_morph_trans[i]; }
                if (bone_morph_rot[i] != OQ.Identity) { local_rot_buf[i] = local_rot_buf[i] * bone_morph_rot[i]; }
            }
        }
        private void collect_bone_morph(ReadOnlySpan<Morph> morphs, int mi, float weight, int depth, int n)
        {
            if (mi < 0 || mi >= morphs.Length || depth > 8) { return; }
            Morph m = morphs[mi];
            if (m.MorphType == MorphType.Bone)
            {
                ReadOnlySpan<BoneMorphElement> els = m.BoneMorphElements.Span;
                for (int i = 0; i < els.Length; i++)
                {
                    int bi = els[i].TargetBone;
                    if (bi < 0 || bi >= n) { continue; }
                    // 位移
                    bone_morph_trans[bi] += new OV3(els[i].Translate.X, els[i].Translate.Y, -els[i].Translate.Z) * weight; // Z翻转（与顶点/动画一致）按权重缩放
                    // 旋转
                    MV4 q = els[i].Quaternion;
                    OQ mq = new OQ(-q.X, -q.Y, q.Z, q.W); // 镜像(x,y,z,w)变(-x,-y,z,w)，按权重slerp后右乘累加
                    mq = mq.Normalized();
                    OQ scaled = OQ.Slerp(OQ.Identity, mq, Math.Clamp(weight, 0f, 1f));
                    bone_morph_rot[bi] = bone_morph_rot[bi] * scaled;
                }
            }
            else if (m.MorphType == MorphType.Group)
            {
                ReadOnlySpan<GroupMorphElement> els = m.GroupMorphElements.Span;
                for (int i = 0; i < els.Length; i++) { collect_bone_morph(morphs, els[i].TargetMorph, weight * els[i].MorphRatio, depth + 1, n); }
            }
        }
        private void update_material_morphs()
        {
            matm_active = false;
            if (!has_material_morph || model == null || current_motion == null
                || current_motion.morph_frame_map.Count == 0 || (!anim_playing && !anim_paused)) { return; }
            int mc = matm_diffuse_mul.Length;
            if (mc == 0) { return; }
            for (int i = 0; i < mc; i++)
            {
                matm_diffuse_mul[i] = new OV4(1f, 1f, 1f, 1f); matm_diffuse_add[i] = OV4.Zero;
                matm_specular_mul[i] = OV3.One; matm_specular_add[i] = OV3.Zero;
                matm_shininess_mul[i] = 1f; matm_shininess_add[i] = 0f;
                matm_ambient_mul[i] = OV3.One; matm_ambient_add[i] = OV3.Zero;
            }
            ReadOnlySpan<Morph> morphs = model.MorphList.Span;
            foreach (KeyValuePair<string, List<morph_keyframe>> kv in current_motion.morph_frame_map)
            {
                if (!morph_name_index.TryGetValue(kv.Key, out int mi)) { continue; }
                float w = interp_morph_weight(kv.Value, current_frame);
                if (MathF.Abs(w) < 1e-5f) { continue; }
                collect_material_morph(morphs, mi, w, 0, mc);
            }
            matm_active = true;
        }
        private void collect_material_morph(ReadOnlySpan<Morph> morphs, int mi, float weight, int depth, int mc)
        {
            if (mi < 0 || mi >= morphs.Length || depth > 8) { return; }
            Morph m = morphs[mi];
            if (m.MorphType == MorphType.Material)
            {
                ReadOnlySpan<MaterialMorphElement> els = m.MaterialMorphElements.Span;
                for (int i = 0; i < els.Length; i++)
                {
                    MaterialMorphElement el = els[i];
                    if (el.Material == -1)
                    {
                        for (int t = 0; t < mc; t++) { accumulate_material_morph(t, el, weight); }
                    }
                    else if (el.Material >= 0 && el.Material < mc) { accumulate_material_morph(el.Material, el, weight); }
                }
            }
            else if (m.MorphType == MorphType.Group)
            {
                ReadOnlySpan<GroupMorphElement> els = m.GroupMorphElements.Span;
                for (int i = 0; i < els.Length; i++) { collect_material_morph(morphs, els[i].TargetMorph, weight * els[i].MorphRatio, depth + 1, mc); }
            }
        }
        private void accumulate_material_morph(int t, MaterialMorphElement el, float w)
        {
            if (el.CalcMode == MaterialMorphCalcMode.Mult)
            {
                OV4 dm = matm_diffuse_mul[t];
                matm_diffuse_mul[t] = new OV4(
                    dm.X * (1f + (el.Diffuse.R - 1f) * w),
                    dm.Y * (1f + (el.Diffuse.G - 1f) * w),
                    dm.Z * (1f + (el.Diffuse.B - 1f) * w),
                    dm.W * (1f + (el.Diffuse.A - 1f) * w));
                OV3 sm = matm_specular_mul[t];
                matm_specular_mul[t] = new OV3(
                    sm.X * (1f + (el.Specular.R - 1f) * w),
                    sm.Y * (1f + (el.Specular.G - 1f) * w),
                    sm.Z * (1f + (el.Specular.B - 1f) * w));
                matm_shininess_mul[t] *= 1f + (el.Shininess - 1f) * w;
                OV3 am = matm_ambient_mul[t];
                matm_ambient_mul[t] = new OV3(
                    am.X * (1f + (el.Ambient.R - 1f) * w),
                    am.Y * (1f + (el.Ambient.G - 1f) * w),
                    am.Z * (1f + (el.Ambient.B - 1f) * w));
            }
            else // Add
            {
                OV4 da = matm_diffuse_add[t];
                matm_diffuse_add[t] = new OV4(da.X + el.Diffuse.R * w, da.Y + el.Diffuse.G * w, da.Z + el.Diffuse.B * w, da.W + el.Diffuse.A * w);
                OV3 sa = matm_specular_add[t];
                matm_specular_add[t] = new OV3(sa.X + el.Specular.R * w, sa.Y + el.Specular.G * w, sa.Z + el.Specular.B * w);
                matm_shininess_add[t] += el.Shininess * w;
                OV3 aa = matm_ambient_add[t];
                matm_ambient_add[t] = new OV3(aa.X + el.Ambient.R * w, aa.Y + el.Ambient.G * w, aa.Z + el.Ambient.B * w);
            }
        }
        private static float interp_morph_weight(List<morph_keyframe> frames, float frame)
        {
            if (frames.Count == 0) { return 0f; }
            int right = upper_bound_frame(frames, frame, frame_of_morph_keyframe);
            int left = right - 1;
            if (left < 0) { return frames[right].weight; }
            if (right >= frames.Count || frames[left].frame == frame) { return frames[left].weight; }
            float denom = frames[right].frame - frames[left].frame;
            if (denom < 0.001f) { return frames[left].weight; }
            float t = (frame - frames[left].frame) / denom;
            return frames[left].weight + (frames[right].weight - frames[left].weight) * t;
        }
        private void load_textures()
        {
            if (model == null) { return; }
            missing_tex = 0;
            missing_tex_names.Clear();
            failed_tex = 0;
            failed_tex_names.Clear();
            disabled_render_tex = 0;
            disabled_render_tex_names.Clear();
            unref_tex = 0;
            unref_tex_names.Clear();
            string? base_dir = Path.GetDirectoryName(file_path);
            if (string.IsNullOrEmpty(base_dir)) { return; }
            ReadOnlySpan<string> list = model.TextureList.Span;
            Dictionary<int, Exception> tex_load_errors = new Dictionary<int, Exception>(); // 纹理索引对应加载异常（按异常类型分类统计）
            for (int i = 0; i < list.Length; i++)
            {
                try { textures[i] = load_texture(Path.Combine(base_dir, list[i])); }
                catch (Exception ex) { tex_load_errors[i] = ex; }
            }
            ReadOnlySpan<Material> mats = model.MaterialList.Span;
            bool[] referenced = new bool[list.Length]; // 标记被任一材质通过主/球/toon(TextureIndex)三个槽位引用的纹理
            HashSet<int> counted = new HashSet<int>();
            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                // 主纹理先标记被引用
                if (mat.Texture >= 0 && mat.Texture < list.Length)
                {
                    referenced[mat.Texture] = true;
                    if (!textures.ContainsKey(mat.Texture) && !counted.Contains(mat.Texture))
                    {
                        counted.Add(mat.Texture);
                        record_tex_failure(tex_load_errors[mat.Texture], list[mat.Texture]);
                    }
                }
                if (mat.SphereTextre >= 0 && mat.SphereTextre < list.Length) // 注：textre为nuget包的作者拼写错误，非本人代码有误。
                {
                    referenced[mat.SphereTextre] = true;
                    if (!textures.ContainsKey(mat.SphereTextre) && !counted.Contains(mat.SphereTextre))
                    {
                        counted.Add(mat.SphereTextre);
                        if (mat.SphereTextureMode == SphereTextureMode.Disabled)
                        {
                            disabled_render_tex++;
                            if (!string.IsNullOrWhiteSpace(list[mat.SphereTextre])) { disabled_render_tex_names.Add(list[mat.SphereTextre]); }
                        }
                        else { record_tex_failure(tex_load_errors[mat.SphereTextre], list[mat.SphereTextre]); }
                    }
                }
                if (mat.SharedToonMode == SharedToonMode.TextureIndex && mat.ToonTexture >= 0
                    && mat.ToonTexture < list.Length)
                {
                    referenced[mat.ToonTexture] = true;
                    try
                    {
                        int toon_tex = load_texture(Path.Combine(base_dir, list[mat.ToonTexture]));
                        if (toon_tex > 0) { toon_textures[i] = toon_tex; }
                    }
                    catch (Exception ex)
                    {
                        if (!counted.Contains(mat.ToonTexture))
                        {
                            counted.Add(mat.ToonTexture);
                            record_tex_failure(ex, list[mat.ToonTexture]);
                        }
                    }
                }
                else if (mat.SharedToonMode == SharedToonMode.SharedToon)
                {
                    int toon_tex = create_shared_toon(mat.ToonTexture);
                    if (toon_tex > 0) { toon_textures[i] = toon_tex; }
                }
            }
            // 纹理表中未被任何材质引用的纹理（不参与渲染）
            for (int i = 0; i < list.Length; i++)
            {
                if (!referenced[i])
                {
                    unref_tex++;
                    if (!string.IsNullOrWhiteSpace(list[i])) { unref_tex_names.Add(list[i]); }
                }
            }
        }
        private void record_tex_failure(Exception ex, string name)
        {
            if (ex is FileNotFoundException)
            {
                missing_tex++;
                if (!string.IsNullOrWhiteSpace(name)) { missing_tex_names.Add(name); }
            }
            else
            {
                failed_tex++;
                if (!string.IsNullOrWhiteSpace(name)) { failed_tex_names.Add(name); }
            }
        }
        private static int load_texture(string path)
        {
            if (!File.Exists(path)) { throw new FileNotFoundException($"纹理文件不存在：{path}", path); }
            try
            {
                if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    int tga_tex = load_tga(path);
                    if (tga_tex > 0) { return tga_tex; }
                }
                byte[] header = new byte[4];
                using (FileStream fs = File.OpenRead(path)) { fs.Read(header, 0, 4); }
                if (header[0] == 'D' && header[1] == 'D' && header[2] == 'S' && header[3] == ' ') { return try_load_dds(path); }
                using Bitmap bmp = new Bitmap(path);
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int tex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tex);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                    bmp.Width, bmp.Height, 0,
                    OG4.PixelFormat.Bgra, PixelType.UnsignedByte, bd.Scan0);
                bmp.UnlockBits(bd);
                // 统一走Repeat环绕 + mipmap + 各向异性过滤
                apply_texture_params(tex, true);
                return tex;
            }
            catch
            {
                try { return load_tga(path); }
                catch { throw new InvalidDataException($"纹理文件解析失败（格式不支持或文件损坏）：{path}"); }
            }
        }
        private static int try_load_dds(string path)
        {
            // DDS文件头定义
            // 8字节处 Flags (bit 0x20000 = 含mipmap)、12字节处 height、16字节处 width；
            // 28字节处 mipmapCount、84字节处 fourCC、88字节处 bpp、128字节处 像素数据起始（4字节魔数+124字节头）
            byte[] data = File.ReadAllBytes(path);
            int flags = BitConverter.ToInt32(data, 8);
            int height = BitConverter.ToInt32(data, 12);
            int width = BitConverter.ToInt32(data, 16);
            int mip_count = 1;
            if ((flags & 0x20000) != 0) { mip_count = Math.Max(1, BitConverter.ToInt32(data, 28)); }
            int four_cc = BitConverter.ToInt32(data, 84);
            bool is_dxt = (four_cc == 0x31545844 || four_cc == 0x33545844 || four_cc == 0x35545844); // DXT1 || DXT3 || DXT5
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            int offset = 128;
            int mw = width, mh = height;
            for (int level = 0; level < mip_count; level++)
            {
                mw = Math.Max(1, width >> level);
                mh = Math.Max(1, height >> level);
                if (is_dxt)
                {
                    int block_size = (four_cc == 0x31545844) ? 8 : 16; // DXT1 = 8，DXT3/5 = 16
                    int internal_fmt;
                    // GL S3TC压缩格式常量（EXT_texture_compression_s3tc）
                    switch (four_cc)
                    {
                        case 0x31545844: // DXT1
                            internal_fmt = 0x83F1; // GL_COMPRESSED_RGBA_S3TC_DXT1_EXT
                            break;
                        case 0x33545844: // DXT3
                            internal_fmt = 0x83F2; // GL_COMPRESSED_RGBA_S3TC_DXT3_EXT
                            break;
                        default: // DXT5
                            internal_fmt = 0x83F3; // GL_COMPRESSED_RGBA_S3TC_DXT5_EXT
                            break;
                    }
                    int size = ((mw + 3) / 4) * ((mh + 3) / 4) * block_size; // 按4x4块向上取整×块字节数
                    if (offset + size > data.Length) { break; }
                    GL.CompressedTexImage2D(TextureTarget.Texture2D, level, (InternalFormat)internal_fmt,
                        mw, mh, 0, size, data[offset..(offset + size)]);
                    offset += size;
                }
                else
                {
                    int bpp = BitConverter.ToInt32(data, 88);
                    // 未压缩DDS像素为BGR(A)字节序，32位含Alpha，否则按24位BGR处理
                    OG4.PixelFormat fmt;
                    PixelInternalFormat internal_fmt;
                    if (bpp == 32)
                    {
                        fmt = OG4.PixelFormat.Bgra;
                        internal_fmt = PixelInternalFormat.Rgba;
                    }
                    else
                    {
                        fmt = OG4.PixelFormat.Bgr;
                        internal_fmt = PixelInternalFormat.Rgb;
                    }
                    int size = mw * mh * bpp / 8;
                    if (offset + size > data.Length) { break; }
                    GL.TexImage2D(TextureTarget.Texture2D, level, internal_fmt,
                        mw, mh, 0, fmt, PixelType.UnsignedByte, data[offset..(offset + size)]);
                    offset += size;
                }
            }
            if (mip_count > 1)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, mip_count - 1);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            }
            else if (!is_dxt)
            {
                while (GL.GetError() != ErrorCode.NoError) { }
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                if (GL.GetError() == ErrorCode.NoError) { GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear); }
                else { GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear); }
            }
            else { GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear); }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            // MMD纹理环绕标准为Repeat，保证UV超出[0,1]的部件正确采样
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            const int tex_max_aniso = 0x84FE; // GL_TEXTURE_MAX_ANISOTROPY_EXT
            const int max_tex_max_aniso = 0x84FF; // GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT
            try
            {
                GL.GetFloat((GetPName)max_tex_max_aniso, out float max_a);
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)tex_max_aniso, Math.Min(16f, max_a));
            }
            catch { }
            return tex;
        }
        private static int load_tga(string path)
        {
            // TGA文件头定义
            // 0 = ID长度、2 = 图像类型(2 = RGB, 3 = 灰度, 10 = RLE-RGB),
            // 12 = 宽度(LE)、14 = 高度(LE)、16 = bpp、17 = 描述(bit5 = 1表示从上往下)
            // 像素数据从18+ID长度处开始，字节序为BGR(A)
            byte[] data = File.ReadAllBytes(path);
            int width = data[13] << 8 | data[12];
            int height = data[15] << 8 | data[14];
            int bpp = data[16];
            int img_type = data[2];
            bool top_down = (data[17] & 0x20) != 0;
            int id_len = data[0];
            int header_len = 18 + id_len;
            int pixel_size = bpp / 8;
            bool rle = img_type == 10;
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            if (!rle && (img_type == 2 || img_type == 3))
            {
                int data_size = width * height * pixel_size;
                if (bpp == 32)
                {
                    byte[] pixels = new byte[data_size];
                    Array.Copy(data, header_len, pixels, 0, data_size);
                    if (!top_down) { flip_v(pixels, width, height, 4); }
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, OG4.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
                }
                else if (bpp == 24)
                {
                    byte[] pixels = new byte[data_size];
                    Array.Copy(data, header_len, pixels, 0, data_size);
                    if (!top_down) { flip_v(pixels, width, height, 3); }
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, width, height, 0, OG4.PixelFormat.Bgr, PixelType.UnsignedByte, pixels);
                }
                else { throw new InvalidDataException($"不支持的TGA像素格式（bpp={bpp}）"); }
            }
            else if (rle && (img_type == 10))
            {
                byte[] pixels = decode_rle_tga(data, header_len, width, height, bpp, top_down);
                if (bpp == 32) { GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, OG4.PixelFormat.Bgra, PixelType.UnsignedByte, pixels); }
                else if (bpp == 24) { GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8, width, height, 0, OG4.PixelFormat.Bgr, PixelType.UnsignedByte, pixels); }
                else { throw new InvalidDataException($"不支持的TGA像素格式（bpp={bpp}）"); }
            }
            else { throw new InvalidDataException($"不支持的TGA图像类型（type={img_type}）"); }
            apply_texture_params(tex, true);
            return tex;
        }
        private static void flip_v(byte[] pixels, int width, int height, int bpp)
        {
            int row_size = width * bpp;
            byte[] row = new byte[row_size];
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * row_size;
                int bot = (height - 1 - y) * row_size;
                Array.Copy(pixels, top, row, 0, row_size);
                Array.Copy(pixels, bot, pixels, top, row_size);
                Array.Copy(row, 0, pixels, bot, row_size);
            }
        }
        private static byte[] decode_rle_tga(byte[] data, int offset, int width, int height, int bpp, bool top_down)
        {
            try
            {
                int pixel_size = bpp / 8;
                byte[] result = new byte[width * height * pixel_size];
                int pos = offset, out_pos = 0, total_pixels = width * height;
                while (out_pos / pixel_size < total_pixels && pos < data.Length)
                {
                    // TGA RLE包
                    // bit7 = 1时重复下一个像素count次，bit7 = 0时直接拷贝count个像素
                    // 包头1字节。bit7(0x80) = 包类型，低7位(0x7F) = count-1（实际像素数比存储值多1）
                    byte header = data[pos++];
                    int count = (header & 0x7F) + 1;
                    bool is_rle = (header & 0x80) != 0;
                    if (is_rle)
                    {
                        for (int i = 0; i < count && out_pos / pixel_size < total_pixels; i++)
                        {
                            for (int b = 0; b < pixel_size; b++) { result[out_pos++] = data[pos + b]; }
                        }
                        pos += pixel_size;
                    }
                    else
                    {
                        for (int i = 0; i < count && out_pos / pixel_size < total_pixels; i++)
                        {
                            for (int b = 0; b < pixel_size; b++) { result[out_pos++] = data[pos++]; }
                        }
                    }
                }
                if (!top_down) { flip_v(result, width, height, pixel_size); }
                return result;
            }
            catch { throw new InvalidDataException("TGA RLE像素数据解码失败"); }
        }
        private static int create_white_texture()
        {
            int t = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, t);
            byte[] w = { 255, 255, 255, 255 };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0,
                OG4.PixelFormat.Rgba, PixelType.UnsignedByte, w);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest); // 1x1像素, 滤波器无影响
            return t;
        }
        private static int create_default_toon_texture()
        {
            const int height = 256;
            byte[] pixels = new byte[height * 4];
            // toon渐变：贴图上传后v=0对应第0行，着色器按t=1-hl采样，故第0行必须是白端（与create_shared_toon的LUT方向一致）
            for (int y = 0; y < height; y++)
            {
                float bright = 1f - y / (float)(height - 1); // 第0行为1（白），最后一行为0（暗）
                // 从白色线性过渡到暗部底色(0.15/0.15/0.18)，蓝通道略高模拟环境光偏蓝
                float r = 0.15f + bright * 0.85f;
                float g = 0.15f + bright * 0.85f;
                float b = 0.18f + bright * 0.82f;
                int idx = y * 4;
                pixels[idx + 0] = (byte)(Math.Clamp(r * 255f, 0, 255));
                pixels[idx + 1] = (byte)(Math.Clamp(g * 255f, 0, 255));
                pixels[idx + 2] = (byte)(Math.Clamp(b * 255f, 0, 255));
                pixels[idx + 3] = 255;
            }
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, height, 0,
                OG4.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return tex;
        }
        // MMD自带的缺省toon数据
        private static readonly byte[] shared_toon_lut = new byte[]
        {
            // toon01
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD,
            0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD,
            0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD,
            0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD, 0xCD,
            // toon02
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1,
            0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1,
            0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1,
            0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1, 0xF5, 0xE1, 0xE1,
            // toon03
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A,
            0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A,
            0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A,
            0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A, 0x9A,
            // toon04
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB,
            0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB,
            0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB,
            0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB, 0xF8, 0xEF, 0xEB,
            // toon05
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFE, 0xFE, 0xFF, 0xFC, 0xFB,
            0xFF, 0xF8, 0xF6, 0xFF, 0xF4, 0xF0, 0xFF, 0xF0, 0xEA, 0xFF, 0xEC, 0xE5,
            0xFF, 0xE8, 0xDF, 0xFF, 0xE7, 0xDE, 0xFF, 0xE7, 0xDD, 0xFF, 0xE7, 0xDD,
            0xFF, 0xE6, 0xDD, 0xFF, 0xE7, 0xDD, 0xFF, 0xE7, 0xDD, 0xFE, 0xE7, 0xDE,
            // toon06
            0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61,
            0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61,
            0xFF, 0xEF, 0x74, 0xFF, 0xFC, 0xE1, 0xFF, 0xF7, 0xB5, 0xFF, 0xEE, 0x68,
            0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61,
            0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61,
            0xFF, 0xED, 0x61, 0xFF, 0xED, 0x61, 0xFA, 0xE7, 0x58, 0xE1, 0xCD, 0x32,
            0xC9, 0xB2, 0x0C, 0xC4, 0xAD, 0x04, 0xC3, 0xAC, 0x03, 0xC3, 0xAC, 0x03,
            0xC3, 0xAC, 0x03, 0xC3, 0xAC, 0x03, 0xC3, 0xAC, 0x03, 0xC3, 0xAC, 0x03,
            // toon07
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            // toon08
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            // toon09
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            // toon10
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
        };
        private static int create_shared_toon(int index)
        {
            const int height = 32;
            if (index < 0) { index = 0; }
            if (index > 9) { index = 9; }
            byte[] pixels = new byte[height * 4];
            int base_idx = index * height * 3;
            for (int y = 0; y < height; y++)
            {
                int src = base_idx + y * 3;
                int idx = y * 4;
                pixels[idx + 0] = shared_toon_lut[src + 0];
                pixels[idx + 1] = shared_toon_lut[src + 1];
                pixels[idx + 2] = shared_toon_lut[src + 2];
                pixels[idx + 3] = 255;
            }
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, height, 0,
                OG4.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return tex;
        }
        private static void apply_texture_params(int tex, bool generate_mipmap)
        {
            GL.BindTexture(TextureTarget.Texture2D, tex);
            // MMD纹理环绕标准为Repeat，UV超出[0,1]的部件靠它正确采样
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            if (generate_mipmap)
            {
                while (GL.GetError() != ErrorCode.NoError) { } // 排空所有残留错误
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                if (GL.GetError() != ErrorCode.NoError) { generate_mipmap = false; }
            }
            if (generate_mipmap)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            // 各向异性过滤常量
            const int tex_max_aniso = 0x84FE; // GL_TEXTURE_MAX_ANISOTROPY_EXT
            const int max_tex_max_aniso = 0x84FF; // GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT
            try
            {
                GL.GetFloat((GetPName)max_tex_max_aniso, out float max_a);
                if (max_a >= 2f) { GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)tex_max_aniso, Math.Min(16f, max_a)); }
            }
            catch { }
        }
        private void delete_model_resources()
        {
            anim_playing = false;
            anim_paused = false;
            current_motion = null;
            display_rest_pose = true;
            bone_rest_local = Array.Empty<OM4>();
            bone_rest_world = Array.Empty<OM4>();
            bone_skinning = Array.Empty<OM4>();
            bone_parent = Array.Empty<int>();
            bone_inv_rest = Array.Empty<OM4>();
            animated_local_buf = Array.Empty<OM4>();
            animated_world_buf = Array.Empty<OM4>();
            done_buf = Array.Empty<bool>();
            bone_topology = Array.Empty<int>();
            local_rot_buf = Array.Empty<OQ>();
            ik_solvers = new List<ik_solver_info>();
            destroy_physics();
            if (vao != 0) { GL.DeleteVertexArray(vao); vao = 0; }
            if (ebo != 0) { GL.DeleteBuffer(ebo); ebo = 0; }
            if (position_vbo != 0) { GL.DeleteBuffer(position_vbo); position_vbo = 0; }
            if (normal_vbo != 0) { GL.DeleteBuffer(normal_vbo); normal_vbo = 0; }
            if (uv_vbo != 0) { GL.DeleteBuffer(uv_vbo); uv_vbo = 0; }
            if (bone_index_vbo != 0) { GL.DeleteBuffer(bone_index_vbo); bone_index_vbo = 0; }
            if (bone_weight_vbo != 0) { GL.DeleteBuffer(bone_weight_vbo); bone_weight_vbo = 0; }
            if (bone_texture != 0) { GL.DeleteTexture(bone_texture); bone_texture = 0; }
            if (bone_buffer != 0) { GL.DeleteBuffer(bone_buffer); bone_buffer = 0; }
            bone_tex_capacity = 0;
            bone_tex_buf = Array.Empty<OM4>();
            for (int i = 0; i < 4; i++) // 清空4个纹理单元（漫反射/球体/toon/骨骼缓冲纹理）
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.TextureBuffer, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            foreach (int id in textures.Values) { if (id > 0 && id != default_texture) { GL.DeleteTexture(id); } }
            textures.Clear();
            foreach (int id in toon_textures.Values) { if (id > 0 && id != default_toon_texture) { GL.DeleteTexture(id); } }
            toon_textures.Clear();
            draw_groups.Clear();
            missing_tex_names.Clear();
        }
        [StructLayout(LayoutKind.Sequential)] // 检查消息队列是否为空
        private struct native_message
        {
            public IntPtr hwnd;
            public uint msg;
            public IntPtr w_param;
            public IntPtr l_param;
            public uint time;
            public int pt_x;
            public int pt_y;
        }
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out native_message m, IntPtr hwnd, uint filter_min, uint filter_max, uint remove_msg);
        private static bool is_app_idle()
        {
            native_message m;
            return !PeekMessage(out m, IntPtr.Zero, 0, 0, 0); // PM_NOREMOVE
        }
        private void application_idle(object? sender, EventArgs e)
        {
            // 仅在消息队列空闲时渲染下一帧，有输入/重绘消息立即退出交还控制权。物理开启时即使未播放也持续驱动模拟。
            while ((anim_playing || physics_active()) && is_app_idle())
            {
                double frame_elapsed = frame_timer.Elapsed.TotalMilliseconds;
                double remain = target_frame_ms - frame_elapsed;
                if (remain > 0) { Thread.Sleep((int)remain); } // 144fps上限的帧间隔控制
                frame_timer.Restart();
                render();
            }
        }
        private void render()
        {
            if (render_busy) { return; } // 嵌套派发的Paint直接跳过本帧
            render_busy = true;
            try
            {
                glControl1.MakeCurrent();
                if (anim_playing && camera_motion != null) { evaluate_camera(current_frame); }
                update_projection(); // 滚轮缩放会改变camera_distance，每帧重算以保持近/远裁面自适应
                OV3 scene_cam_pos;
                bool scene_drawn = draw_scene(out scene_cam_pos);
                if (!scene_drawn)
                {
                    glControl1.SwapBuffers();
                    return;
                }
                if (current_motion == null && !anim_playing && !anim_paused && !checkBox3.Checked)
                { label3.Text = $"镜头坐标  X={scene_cam_pos.X:F1}  Y={scene_cam_pos.Y:F1}  Z={scene_cam_pos.Z:F1}"; }
                glControl1.SwapBuffers();
                // 动画播放或物理模拟时驱动下一帧渲染，限制144fps上限
                double advance_gap = advance_timer.Elapsed.TotalMilliseconds;
                advance_timer.Restart();
                if (anim_playing && (current_motion != null || camera_motion != null))
                {
                    if (advance_gap > suspend_gap_ms)
                    { anim_frame_offset -= (float)(advance_gap / 1000.0) * 30f; }
                    float anim_elapsed = (float)anim_clock.Elapsed.TotalSeconds;
                    current_frame = anim_frame_offset + anim_elapsed * 30f;
                    int total_max = playback_max_frame();
                    if (current_frame >= total_max)
                    {
                        current_frame = total_max - 1;
                        anim_playing = false;
                        anim_paused = false;
                        update_playback_buttons();
                    }
                    else
                    {
                        if (current_motion != null) { compute_bone_transforms(); } // 只加载镜头时无需骨骼解算
                        else if (physics_active()) { apply_rest_pose_buffers(); } // 只放镜头时物理基于rest pose模拟
                        if (physics_active()) { step_physics(advance_gap); }
                        anim_fps_count++;
                        double fps_elapsed = (DateTime.Now - anim_fps_time).TotalSeconds;
                        if (fps_elapsed >= 0.5)
                        {
                            anim_current_fps = (int)(anim_fps_count / fps_elapsed);
                            anim_fps_count = 0;
                            anim_fps_time = DateTime.Now;
                        }
                        update_playback_label();
                    }
                }
                else if (physics_active())
                {
                    if (current_motion != null && !display_rest_pose) { compute_bone_transforms(); }
                    else { apply_rest_pose_buffers(); }
                    step_physics(advance_gap);
                }
            }
            finally { render_busy = false; }
        }
        private int playback_max_frame() // 动作与镜头的总帧长（取较大者）
        {
            int mf = 0;
            if (current_motion != null && current_motion.max_frame > mf) { mf = current_motion.max_frame; }
            if (camera_motion != null && camera_motion.max_frame > mf) { mf = camera_motion.max_frame; }
            return mf;
        }
        private bool draw_scene(out OV3 cam_pos)
        {
            cam_pos = OV3.Zero;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthFunc(DepthFunction.Lequal); // LEqual让后绘制的共面材质（贴花层，如外套花纹）能覆盖先绘制的，与MMD按材质顺序叠加的行为一致，消除共面深度冲突产生的摩尔纹
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(TriangleFace.Back);
            GL.DepthMask(true);
            GL.ClearColor(new Color4(0.15f, 0.15f, 0.15f, 1f)); // 深灰色背景
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            if (model == null || draw_groups.Count == 0) { return false; }
            OV3 forward = OV3.Transform(-OV3.UnitZ, camera_rotation);
            OV3 up = OV3.Transform(OV3.UnitY, camera_rotation);
            cam_pos = camera_target - forward * camera_distance;
            OM4 model_mat = OM4.CreateTranslation(-model_center);
            OM4 view = OM4.LookAt(cam_pos, camera_target, up);
            OM4 mvp = model_mat * view * projection;
            GL.UseProgram(shader_program);
            GL.UniformMatrix4(uloc_mvp, true, ref mvp);
            GL.UniformMatrix4(uloc_model, true, ref model_mat);
            GL.UniformMatrix4(uloc_view, true, ref view);
            GL.Uniform3(uloc_light_pos, OV3.Normalize(new OV3(0.5f, 1.0f, 0.5f)) * 1000f); // MMD默认光从观众侧右前上方照来（GL世界空间来向）
            GL.Uniform3(uloc_light_color, new OV3(0.55f, 0.55f, 0.55f)); // MMD默认灯光颜色
            GL.Uniform3(uloc_view_pos, cam_pos);
            GL.Uniform1(uloc_use_lighting, lighting_enabled ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(uloc_tex_sampler, 0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.Uniform1(uloc_sphere_sampler, 1);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.Uniform1(uloc_toon_sampler, 2);
            GL.BindVertexArray(vao);
            update_morphs();
            update_material_morphs();
            // 上传骨骼蒙皮矩阵到缓冲纹理
            if (bone_skinning.Length > 0)
            {
                int bcount = Math.Min(bone_skinning.Length, bone_tex_capacity);
                for (int bi = 0; bi < bcount; bi++) { bone_tex_buf[bi] = OM4.Transpose(bone_skinning[bi]); }
                GL.BindBuffer(BufferTarget.TextureBuffer, bone_buffer);
                GL.BufferSubData(BufferTarget.TextureBuffer, IntPtr.Zero, bcount * 16 * sizeof(float), bone_tex_buf);
                GL.BindBuffer(BufferTarget.TextureBuffer, 0);
            }
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.TextureBuffer, bone_texture);
            GL.Uniform1(uloc_bone_tex, 3);
            GL.Uniform1(uloc_bone_tex_width, bone_tex_capacity);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.PolygonOffsetFill);
            // 绘制不透明物体（Alpha >= 0.5），保持深度写入
            int opaque_order = 0;
            for (int i = 0; i < draw_groups.Count; i++)
            {
                (int first_index, int index_count, Material material) = draw_groups[i];
                if (material.Diffuse.A < 0.5f) { continue; }
                GL.PolygonOffset(1f, -(float)opaque_order);
                opaque_order++;
                draw_material(first_index, index_count, material, i);
            }
            GL.Disable(EnableCap.PolygonOffsetFill);
            // 绘制透明物体（Alpha<0.5），不写深度
            GL.DepthMask(false);
            for (int i = 0; i < draw_groups.Count; i++)
            {
                (int first_index, int index_count, Material material) = draw_groups[i];
                if (material.Diffuse.A >= 0.5f) { continue; }
                draw_material(first_index, index_count, material, i);
            }
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.BindVertexArray(0);
            GL.ActiveTexture(TextureUnit.Texture0);
            return true;
        }
        private void evaluate_camera(float frame) // 求值VMD镜头并覆盖相机参数（babylon-mmd）
        {
            if (camera_motion == null || camera_motion.camera_frames.Count == 0) { return; }
            List<camera_keyframe> frames = camera_motion.camera_frames;
            // 二分查找右侧关键帧（第一个帧号>目标帧的下标）
            int right = upper_bound_frame(frames, frame, frame_of_camera_keyframe);
            int left = right - 1;
            OV3 target_mmd;
            OV3 rot_mmd;
            float dist_mmd;
            float fov_deg;
            if (left < 0) // 首关键帧之前
            {
                camera_keyframe f = frames[0];
                target_mmd = f.position; rot_mmd = f.rotation; dist_mmd = f.distance; fov_deg = f.fov;
            }
            else if (right >= frames.Count || frames[left].frame + 1 == frames[right].frame)
            {
                // 尾帧之后，或相邻帧号差1为镜头硬切（MMD约定不插值，保持左帧直到切换）
                camera_keyframe f = frames[left];
                target_mmd = f.position; rot_mmd = f.rotation; dist_mmd = f.distance; fov_deg = f.fov;
            }
            else
            {
                camera_keyframe a = frames[left];
                camera_keyframe b = frames[right];
                float t = (frame - a.frame) / (b.frame - a.frame);
                bezier_curve[] ip = b.interp; // 曲线参数取右侧关键帧
                float wx = bezier_eval(ip[0].x1, ip[0].x2, ip[0].y1, ip[0].y2, t);
                float wy = bezier_eval(ip[1].x1, ip[1].x2, ip[1].y1, ip[1].y2, t);
                float wz = bezier_eval(ip[2].x1, ip[2].x2, ip[2].y1, ip[2].y2, t);
                float wr = bezier_eval(ip[3].x1, ip[3].x2, ip[3].y1, ip[3].y2, t);
                float wd = bezier_eval(ip[4].x1, ip[4].x2, ip[4].y1, ip[4].y2, t);
                float wf = bezier_eval(ip[5].x1, ip[5].x2, ip[5].y1, ip[5].y2, t);
                target_mmd = new OV3(
                    a.position.X + (b.position.X - a.position.X) * wx,
                    a.position.Y + (b.position.Y - a.position.Y) * wy,
                    a.position.Z + (b.position.Z - a.position.Z) * wz);
                // 欧拉角直接lerp（MMD用角度超360°编码多圈，不能用四元数slerp）
                rot_mmd = new OV3(
                    a.rotation.X + (b.rotation.X - a.rotation.X) * wr,
                    a.rotation.Y + (b.rotation.Y - a.rotation.Y) * wr,
                    a.rotation.Z + (b.rotation.Z - a.rotation.Z) * wr);
                dist_mmd = a.distance + (b.distance - a.distance) * wd;
                fov_deg = a.fov + (b.fov - a.fov) * wf;
            }
            // MMD转GL坐标，注视点Z取反并减去model_center对齐模型平移；
            // 旋转为Qy(-ry)Qx(-rx)Qz(-rz)，经Z翻转共轭(x,y,z,w)变(-x,-y,z,w)后 = Qy(ry)Qx(rx)Qz(-rz)；距离取反（babylon-mmd）
            camera_target = new OV3(target_mmd.X, target_mmd.Y, -target_mmd.Z) - model_center;
            OQ qy = OQ.FromAxisAngle(OV3.UnitY, rot_mmd.Y);
            OQ qx = OQ.FromAxisAngle(OV3.UnitX, rot_mmd.X);
            OQ qz = OQ.FromAxisAngle(OV3.UnitZ, -rot_mmd.Z);
            camera_rotation = qy * qx * qz;
            camera_distance = -dist_mmd;
            camera_fov = fov_deg;
        }
        private void save_screenshot(string path) // 离屏FBO渲染3840×2160后保存到文件
        {
            glControl1.MakeCurrent();
            // 超出显卡renderbuffer上限时按16:9等比降低
            int max_rb = GL.GetInteger(GetPName.MaxRenderbufferSize);
            int shot_w = 3840, shot_h = 2160;
            if (max_rb < shot_w)
            {
                shot_w = max_rb & ~3;
                shot_h = shot_w * 9 / 16;
            }
            int fbo = GL.GenFramebuffer();
            int color_rb = GL.GenRenderbuffer();
            int depth_rb = GL.GenRenderbuffer();
            try
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, color_rb);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, shot_w, shot_h);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, color_rb);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth_rb);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, shot_w, shot_h);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth_rb);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
                if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                { throw new Exception($"离屏缓冲区创建失败（{shot_w}×{shot_h}）"); }
                GL.Viewport(0, 0, shot_w, shot_h);
                // 临时改为16:9投影
                OM4 saved_projection = projection;
                projection = build_projection((float)shot_w / shot_h);
                OV3 shot_cam_pos;
                draw_scene(out shot_cam_pos);
                projection = saved_projection;
                // 读回像素并写入Bitmap
                Bitmap bmp = new Bitmap(shot_w, shot_h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, shot_w, shot_h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                GL.ReadPixels(0, 0, shot_w, shot_h, OG4.PixelFormat.Bgr, PixelType.UnsignedByte, bd.Scan0);
                GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
                bmp.UnlockBits(bd);
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
                string ext = Path.GetExtension(path).ToLower();
                if (ext == ".jpg" || ext == ".jpeg") { bmp.Save(path, ImageFormat.Jpeg); }
                else if (ext == ".bmp") { bmp.Save(path, ImageFormat.Bmp); }
                else { bmp.Save(path, ImageFormat.Png); }
                bmp.Dispose();
            }
            finally
            {
                // 无论成败失败都恢复默认帧缓冲与窗口视口
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.DeleteFramebuffer(fbo);
                GL.DeleteRenderbuffer(color_rb);
                GL.DeleteRenderbuffer(depth_rb);
                GL.Viewport(0, 0, glControl1.ClientSize.Width, glControl1.ClientSize.Height);
                update_projection();
                glControl1.Invalidate();
            }
        }

        private string format_time(float seconds)
        {
            return TimeSpan.FromSeconds((int)seconds).ToString(@"mm\:ss");
        }

        private void update_playback_label()
        {
            if ((current_motion == null && camera_motion == null) || checkBox3.Checked) { return; } // 开启自检时label3保留自检结果，不被FPS覆盖
            int total_frames = playback_max_frame();
            int cur = (int)current_frame;
            float total_sec = total_frames / 30f;
            float cur_sec = current_frame / 30f;
            label3.Text = $"{anim_current_fps} fps, {cur}/{total_frames}, {format_time(cur_sec)}/{format_time(total_sec)}";
        }

        private void setup_animation()
        {
            if (model == null || current_motion == null) { return; }
            if (bone_rest_local.Length != model.BoneList.Span.Length) { setup_bone_data(); } // 兜底，正常在加载模型时已构建
            // 确定哪些骨骼需要位置动画，同时构建按骨骼名分组的帧索引
            movable_bones = new HashSet<string>();
            bone_frame_map = new Dictionary<string, List<bone_keyframe>>();
            foreach (bone_keyframe kf in current_motion.bone_frames)
            {
                if (kf.position.X != 0f || kf.position.Y != 0f || kf.position.Z != 0f)
                { movable_bones.Add(kf.bone_name); }
                if (!bone_frame_map.TryGetValue(kf.bone_name, out List<bone_keyframe>? list))
                {
                    list = new List<bone_keyframe>();
                    bone_frame_map[kf.bone_name] = list;
                }
                list.Add(kf);
            }
            // 每根骨骼的关键帧按帧号排序
            foreach (List<bone_keyframe> kf_list in bone_frame_map.Values) { kf_list.Sort(compare_frame); }
            for (int i = 0; i < bone_skinning.Length; i++) { bone_skinning[i] = OM4.Identity; }
            phys_needs_reset = true; // 动作更换后刚体传送到新姿势
            anim_playing = false;
            anim_paused = false;
            current_frame = 0;
        }
        private void setup_bone_data() // 构建骨骼层级/IK/继承/变形顺序等模型级数据（加载模型时调用）
        {
            if (model == null) { return; }
            ReadOnlySpan<Bone> bones = model.BoneList.Span;
            int n = bones.Length;
            bone_rest_local = new OM4[n];
            bone_rest_world = new OM4[n];
            bone_skinning = new OM4[n];
            bone_parent = new int[n];
            bone_inv_rest = new OM4[n];
            animated_local_buf = new OM4[n];
            animated_world_buf = new OM4[n];
            done_buf = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bone_parent[i] = bones[i].ParentBone;
                MV3 world_pos = bones[i].Position;
                OV3 world = new OV3(world_pos.X, world_pos.Y, -world_pos.Z);
                OV3 local = world;
                if (bone_parent[i] >= 0)
                {
                    MV3 parent_world = bones[bone_parent[i]].Position;
                    local -= new OV3(parent_world.X, parent_world.Y, -parent_world.Z);
                }
                bone_rest_local[i] = OM4.CreateTranslation(local);
            }
            // 计算rest pose世界变换和拓扑序（多遍传播，确保父先于子）
            bool[] rest_done = new bool[n];
            int rest_remaining = n;
            bone_topology = new int[n];
            int topo_count = 0;
            while (rest_remaining > 0)
            {
                bool progress = false;
                for (int i = 0; i < n; i++)
                {
                    if (rest_done[i]) { continue; }
                    int p = bone_parent[i];
                    if (p < 0 || rest_done[p])
                    {
                        if (p < 0) { bone_rest_world[i] = bone_rest_local[i]; }
                        else { bone_rest_world[i] = bone_rest_local[i] * bone_rest_world[p]; }
                        bone_inv_rest[i] = bone_rest_world[i].Inverted();
                        bone_topology[topo_count++] = i;
                        rest_done[i] = true;
                        rest_remaining--;
                        progress = true;
                    }
                }
                if (!progress) { break; }
            }
            // 构建IK解算器
            local_rot_buf = new OQ[n];
            ik_solvers = new List<ik_solver_info>();
            for (int i = 0; i < n; i++)
            {
                if (((short)bones[i].BoneFlag & 0x0020) == 0) { continue; } // IK标志
                if (bones[i].IKLinks.Length == 0) { continue; }
                if (bones[i].IKTarget < 0 || bones[i].IKTarget >= n) { continue; }
                ik_solver_info solver = new ik_solver_info();
                solver.ik_bone = i;
                solver.target_bone = bones[i].IKTarget;
                solver.iterations = Math.Min(bones[i].IterCount, 256);
                solver.limit_angle = Math.Abs(bones[i].MaxRadianPerIter);
                if (solver.limit_angle < 0.001f) { solver.limit_angle = MathF.PI; }
                ReadOnlySpan<IKLink> links = bones[i].IKLinks.Span;
                solver.chains = new List<ik_chain_info>(links.Length);
                // 限制角min/max交换、旋转顺序与限制轴判定（babylon-mmd）
                for (int li = 0; li < links.Length; li++)
                {
                    int lb = links[li].Bone;
                    if (lb < 0 || lb >= n) { continue; }
                    ik_chain_info chain = new ik_chain_info();
                    chain.bone = lb;
                    chain.has_limit = links[li].IsEnableAngleLimited;
                    if (chain.has_limit)
                    {
                        // PMX空间角度限制转GL空间（Z镜像，x/y反转符号，z不变）
                        MV3 pmin = links[li].MinLimit, pmax = links[li].MaxLimit;
                        chain.min_angle = new OV3(-pmax.X, -pmax.Y, pmin.Z);
                        chain.max_angle = new OV3(-pmin.X, -pmin.Y, pmax.Z);
                        // 确保min≤max
                        if (chain.min_angle.X > chain.max_angle.X) { (chain.min_angle.X, chain.max_angle.X) = (chain.max_angle.X, chain.min_angle.X); }
                        if (chain.min_angle.Y > chain.max_angle.Y) { (chain.min_angle.Y, chain.max_angle.Y) = (chain.max_angle.Y, chain.min_angle.Y); }
                        if (chain.min_angle.Z > chain.max_angle.Z) { (chain.min_angle.Z, chain.max_angle.Z) = (chain.max_angle.Z, chain.min_angle.Z); }
                        // 确定欧拉旋转顺序
                        float half_pi = MathF.PI * 0.5f;
                        if (-half_pi < chain.min_angle.X && chain.max_angle.X < half_pi)
                        { chain.rotation_order = 0; } // YXZ
                        else if (-half_pi < chain.min_angle.Y && chain.max_angle.Y < half_pi)
                        { chain.rotation_order = 1; } // ZYX
                        else { chain.rotation_order = 2; } // XZY
                        // 确定限制轴
                        OV3 r = chain.min_angle;
                        OV3 s = chain.max_angle;
                        if (r.X == 0 && s.X == 0 && r.Y == 0 && s.Y == 0 && r.Z == 0 && s.Z == 0)
                        { chain.solve_axis = 4; } // Fixed（跳过求解）
                        else if (r.Y == 0 && s.Y == 0 && r.Z == 0 && s.Z == 0)
                        { chain.solve_axis = 1; } // X
                        else if (r.X == 0 && s.X == 0 && r.Z == 0 && s.Z == 0)
                        { chain.solve_axis = 2; } // Y
                        else if (r.X == 0 && s.X == 0 && r.Y == 0 && s.Y == 0)
                        { chain.solve_axis = 3; } // Z
                        else { chain.solve_axis = 0; } // None
                    }
                    else { chain.solve_axis = 0; chain.rotation_order = 2; }
                    solver.chains.Add(chain);
                }
                if (solver.chains.Count == 0) { continue; }
                // 构建update_path。从target向上遍历父骨骼直到收集完所有链骨骼
                List<int> path = new List<int>();
                int cur = solver.target_bone;
                int collected = 0;
                HashSet<int> chain_set = new HashSet<int>();
                for (int ci = 0; ci < solver.chains.Count; ci++) { chain_set.Add(solver.chains[ci].bone); }
                while (cur >= 0 && collected < solver.chains.Count)
                {
                    if (chain_set.Contains(cur)) { collected++; }
                    cur = bone_parent[cur];
                }
                // 再从target向上遍历一次到top（cur的父），反向即为root到target
                cur = solver.target_bone;
                int top = cur;
                int countdown = solver.chains.Count;
                while (cur >= 0 && countdown > 0)
                {
                    if (chain_set.Contains(cur)) { countdown--; }
                    top = cur;
                    cur = bone_parent[cur];
                }
                // 从target向上直到遇到top的父（含top，不含其上）
                path.Clear();
                cur = solver.target_bone;
                while (cur >= 0)
                {
                    path.Add(cur);
                    if (cur == top) { break; }
                    cur = bone_parent[cur];
                }
                path.Reverse(); // 反转成root到target顺序
                solver.update_path = path.ToArray();
                // 记录每条链骨骼在路径中的位置
                solver.path_position = new int[solver.chains.Count];
                for (int ci = 0; ci < solver.chains.Count; ci++)
                {
                    int lb = solver.chains[ci].bone;
                    int pos = -1;
                    for (int pi = 0; pi < solver.update_path.Length; pi++)
                    {
                        if (solver.update_path[pi] == lb) { pos = pi; break; }
                    }
                    solver.path_position[ci] = pos;
                }
                ik_solvers.Add(solver);
            }
            // 构建继承源(append/grant)数据与MMD变形顺序
            bone_append_source = new int[n];
            bone_append_ratio = new float[n];
            bone_append_rot = new bool[n];
            bone_append_pos = new bool[n];
            bone_anim_offset = new OV3[n];
            bone_transform_depth = new int[n];
            bone_after_physics = new int[n];
            for (int i = 0; i < n; i++)
            {
                bone_append_source[i] = -1;
                short flag = (short)bones[i].BoneFlag;
                bool rot_att = (flag & 0x0100) != 0;   // RotationAttach
                bool pos_att = (flag & 0x0200) != 0;   // TranslationAttach
                if (rot_att || pos_att)
                {
                    int ap = bones[i].AttatchParent;
                    if (ap >= 0 && ap < n)
                    {
                        bone_append_source[i] = ap;
                        bone_append_ratio[i] = bones[i].AttatchRatio;
                        bone_append_rot[i] = rot_att;
                        bone_append_pos[i] = pos_att;
                    }
                }
                bone_transform_depth[i] = bones[i].TransformDepth;
                bone_after_physics[i] = (flag & 0x1000) != 0 ? 1 : 0;   // TransformAfterPhysics
            }
            // 骨骼索引对应IK解算器（在变形顺序中到达该IK骨时触发求解）
            bone_ik_solver = new ik_solver_info?[n];
            for (int si = 0; si < ik_solvers.Count; si++) { bone_ik_solver[ik_solvers[si].ik_bone] = ik_solvers[si]; }
            // MMD变形顺序按(是否物理后, TransformDepth, 骨骼索引)排序
            deform_order = new int[n];
            for (int i = 0; i < n; i++) { deform_order[i] = i; }
            Array.Sort(deform_order, compare_deform);
            for (int i = 0; i < n; i++) { bone_skinning[i] = OM4.Identity; }
        }
        private int compare_deform(int a, int b)
        {
            // 物理后骨排最后，其次按TransformDepth，最后按骨骼索引
            if (bone_after_physics[a] != bone_after_physics[b]) { return bone_after_physics[a] - bone_after_physics[b]; }
            if (bone_transform_depth[a] != bone_transform_depth[b]) { return bone_transform_depth[a] - bone_transform_depth[b]; }
            return a - b;
        }

        private void reset_to_rest_pose()
        {
            for (int i = 0; i < bone_skinning.Length; i++) { bone_skinning[i] = OM4.Identity; }
        }
        private bool physics_active()
        {
            return checkBox4.Checked && phys_world != null && bone_skinning.Length > 0; // 物理模拟是否生效（勾选且物理世界已创建）
        }
        private void apply_rest_pose_buffers() // 未播放动作时物理模拟的基准姿势
        {
            int n = bone_rest_local.Length;
            if (animated_local_buf.Length != n || animated_world_buf.Length != n) { return; }
            for (int i = 0; i < n; i++)
            {
                animated_local_buf[i] = bone_rest_local[i];
                animated_world_buf[i] = bone_rest_world[i];
            }
        }

        // 物理引擎（BulletSharp）部分子程序
        // 参考babylon-mmd、saba开源项目的MMD物理实现。物理世界直接建在GL镜像系（Z取反），镜像下动力学等价。
        private static EM4 om4_to_em4(OM4 m) // OpenTK转Evergine矩阵
        {
            return MemoryMarshal.Cast<OM4, EM4>(MemoryMarshal.CreateSpan(ref m, 1))[0];
        }
        private static OM4 em4_to_om4(EM4 m) // Evergine转OpenTK矩阵
        {
            return MemoryMarshal.Cast<EM4, OM4>(MemoryMarshal.CreateSpan(ref m, 1))[0];
        }
        private static OQ pmx_euler_to_gl_quat(MV3 rot)
        {
            // PMX刚体/关节的欧拉角（弧度，D3DX YawPitchRoll顺序，先Z后X再Y）转GL系四元数
            // 镜像法则(x,y,z,w)变(-x,-y,z,w)作用到基旋转上，Qx(θ)变Qx(-θ)、Qy(θ)变Qy(-θ)、Qz(θ)不变
            return OQ.FromAxisAngle(OV3.UnitY, -rot.Y) * OQ.FromAxisAngle(OV3.UnitX, -rot.X) * OQ.FromAxisAngle(OV3.UnitZ, rot.Z);
        }
        private void checkBox4_CheckedChanged(object? sender, EventArgs e)
        {
            if (checkBox4.Checked)
            {
                phys_needs_reset = true; // 开启时把刚体传送到当前姿势
                glControl1.Invalidate();
            }
            else if (!anim_playing)
            {
                // 关闭时恢复纯动画姿势（播放中无需处理，下一帧自动重算）
                if (current_motion != null && !display_rest_pose) { compute_bone_transforms(); }
                else { reset_to_rest_pose(); }
                glControl1.Invalidate();
            }
        }
        private static bool joint_locked_6dof(Joint jt) // 6个自由度上下限全为0的关节（PMX中以刚性固定表示）
        {
            return jt.TranslationMinLimit.X == 0f && jt.TranslationMinLimit.Y == 0f && jt.TranslationMinLimit.Z == 0f
                && jt.TranslationMaxLimit.X == 0f && jt.TranslationMaxLimit.Y == 0f && jt.TranslationMaxLimit.Z == 0f
                && jt.RotationRadianMinLimit.X == 0f && jt.RotationRadianMinLimit.Y == 0f && jt.RotationRadianMinLimit.Z == 0f
                && jt.RotationRadianMaxLimit.X == 0f && jt.RotationRadianMaxLimit.Y == 0f && jt.RotationRadianMaxLimit.Z == 0f;
        }
        private void create_physics() // 按PMX刚体/关节构建Bullet物理世界（调用之前请先完成setup_bone_data的骨骼数据）
        {
            destroy_physics();
            if (model == null || model.RigidBodyList.Length == 0) { return; }
            try
            {
                ReadOnlySpan<MRB> rbs = model.RigidBodyList.Span;
                int count = rbs.Length;
                phys_config = new DefaultCollisionConfiguration();
                phys_dispatcher = new CollisionDispatcher(phys_config);
                phys_broadphase = new DbvtBroadphase();
                phys_solver = new SequentialImpulseConstraintSolver();
                phys_world = new DiscreteDynamicsWorld(phys_dispatcher, phys_broadphase, phys_solver, phys_config);
                phys_world.Gravity = new EV3(0f, -98f, 0f); // MMD重力-9.8×10（MMD长度单位约相当于0.1m）
                // 地面（y = 0静态平面，与所有碰撞组碰撞，对应MMD默认开启的地面物理）
                StaticPlaneShape ground_shape = new StaticPlaneShape(new EV3(0f, 1f, 0f), 0f);
                phys_shapes.Add(ground_shape);
                RigidBodyConstructionInfo ground_info = new RigidBodyConstructionInfo(0f, null!, ground_shape);
                phys_ground = new BRB(ground_info);
                ground_info.Dispose();
                phys_world.AddRigidBody(phys_ground, 0xFFFF, 0xFFFF);
                // 质量归一化
                float max_dyn_mass = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (rbs[i].PhysicsType != RigidBodyPhysicsType.Static && rbs[i].Mass > max_dyn_mass) { max_dyn_mass = rbs[i].Mass; }
                }
                float mass_scale = max_dyn_mass > 100f ? 100f / max_dyn_mass : 1f;
                ReadOnlySpan<Joint> joints = model.JointList.Span;
                int[] parent_joint_count = new int[count]; // 每个刚体作为RigidBody2被多少关节约束（多父 = 网状交叉链，不可焊接）
                bool[] weld_body = new bool[count];
                for (int i = 0; i < joints.Length; i++)
                {
                    int b0 = joints[i].RigidBody2;
                    if (b0 >= 0 && b0 < count) { parent_joint_count[b0]++; }
                }
                for (int i = 0; i < joints.Length; i++)
                {
                    Joint jt0 = joints[i];
                    int a0 = jt0.RigidBody1;
                    int b1 = jt0.RigidBody2;
                    if (a0 < 0 || a0 >= count || b1 < 0 || b1 >= count) { continue; }
                    if (rbs[b1].PhysicsType == RigidBodyPhysicsType.Static) { continue; }
                    if (parent_joint_count[b1] != 1) { continue; }
                    if (!joint_locked_6dof(jt0)) { continue; }
                    weld_body[b1] = true;
                }
                phys_body_bone = new int[count];
                phys_body_mode = new int[count];
                phys_body_offset = new OM4[count];
                phys_body_offset_inv = new OM4[count];
                phys_body_initial = new OM4[count];
                phys_bone_world = new OM4[bone_skinning.Length];
                phys_bone_flag = new byte[bone_skinning.Length];
                for (int i = 0; i < count; i++)
                {
                    MRB rb = rbs[i];
                    CollisionShape shape;
                    bool zero_volume; // 任一尺寸为0的刚体不参与碰撞响应，避免零体积形状导致求解异常（babylon-mmd）
                    switch (rb.Shape)
                    {
                        case RigidBodyShape.Sphere:
                            shape = new SphereShape(rb.Size.X);
                            zero_volume = rb.Size.X == 0f;
                            break;
                        case RigidBodyShape.Box:
                            shape = new BoxShape(rb.Size.X, rb.Size.Y, rb.Size.Z); // PMX盒尺寸即半宽，与Bullet一致
                            zero_volume = rb.Size.X == 0f || rb.Size.Y == 0f || rb.Size.Z == 0f;
                            break;
                        default: // Capsule
                            shape = new CapsuleShape(rb.Size.X, rb.Size.Y); // 半径，圆柱段高度
                            zero_volume = rb.Size.X == 0f || rb.Size.Y == 0f;
                            break;
                    }
                    phys_shapes.Add(shape);
                    // 初始世界变换（GL系）
                    OM4 initial = OM4.CreateFromQuaternion(pmx_euler_to_gl_quat(rb.RotationRadian))
                        * OM4.CreateTranslation(new OV3(rb.Position.X, rb.Position.Y, -rb.Position.Z)); // 位置Z取反，欧拉角按镜像法则转四元数
                    phys_body_initial[i] = initial;
                    int bone = (rb.Bone >= 0 && rb.Bone < bone_inv_rest.Length) ? rb.Bone : -1;
                    phys_body_bone[i] = bone;
                    phys_body_mode[i] = (int)rb.PhysicsType;
                    if (bone >= 0)
                    {
                        phys_body_offset[i] = initial * bone_inv_rest[bone]; // 刚体在骨骼局部空间的固定位姿
                        phys_body_offset_inv[i] = phys_body_offset[i].Inverted();
                    }
                    else
                    {
                        phys_body_offset[i] = OM4.Identity;
                        phys_body_offset_inv[i] = OM4.Identity;
                    }
                    bool kinematic = rb.PhysicsType == RigidBodyPhysicsType.Static || weld_body[i]; // 静态/焊接体均按运动学刚体（跟随骨骼）建立
                    float mass = kinematic ? 0f : rb.Mass * mass_scale;
                    EV3 inertia = new EV3(0f, 0f, 0f);
                    if (mass > 0f) { inertia = shape.CalculateLocalInertia(mass); }
                    DefaultMotionState motion = new DefaultMotionState(om4_to_em4(initial));
                    RigidBodyConstructionInfo info = new RigidBodyConstructionInfo(mass, motion, shape, inertia);
                    info.LinearDamping = rb.TranslationAttenuation;
                    info.AngularDamping = rb.RotationAttenuation;
                    info.Friction = rb.Friction;
                    info.Restitution = rb.Recoil;
                    info.AdditionalDamping = true; // MMD风格的附加阻尼，抑制低速抖动
                    BRB body = new BRB(info);
                    info.Dispose();
                    if (kinematic) { body.CollisionFlags |= CollisionFlags.KinematicObject; }
                    if (zero_volume) { body.CollisionFlags |= CollisionFlags.NoContactResponse; }
                    body.ActivationState = ActivationState.DisableDeactivation; // 刚体不休眠，骨骼随时在动
                    phys_world.AddRigidBody(body, 1 << rb.Group, rb.GroupTarget); // 碰撞组 = 1<<组号，掩码直接用PMX的GroupTarget
                    phys_bodies.Add(body);
                }
                // 关节全部按PMX 2.0的6自由度弹簧约束处理
                for (int i = 0; i < joints.Length; i++)
                {
                    Joint jt = joints[i];
                    int a = jt.RigidBody1;
                    int b = jt.RigidBody2;
                    if (a < 0 || a >= phys_bodies.Count || b < 0 || b >= phys_bodies.Count) { continue; }
                    if (weld_body[b]) // 六轴全锁死关节
                    {
                        phys_weld_a.Add(a);
                        phys_weld_b.Add(b);
                        phys_weld_rel.Add(phys_body_initial[b] * phys_body_initial[a].Inverted());
                        continue;
                    }
                    OM4 joint_world = OM4.CreateFromQuaternion(pmx_euler_to_gl_quat(jt.RotationRadian))
                        * OM4.CreateTranslation(new OV3(jt.Position.X, jt.Position.Y, -jt.Position.Z));
                    // 关节世界位姿变换到两刚体各自的局部空间
                    OM4 frame_a = joint_world * phys_body_initial[a].Inverted();
                    OM4 frame_b = joint_world * phys_body_initial[b].Inverted();
                    Generic6DofSpringConstraint con = new Generic6DofSpringConstraint(
                        phys_bodies[a], phys_bodies[b], om4_to_em4(frame_a), om4_to_em4(frame_b), true);
                    for (int axis = 0; axis < 6; axis++) { con.SetParam(ConstraintParam.StopErp, 0.475f, axis); } // 数值来自babylon-mmd
                    // 平移限制
                    // Z轴镜像，区间取反并交换上下限
                    con.LinearLowerLimit = new EV3(jt.TranslationMinLimit.X, jt.TranslationMinLimit.Y, -jt.TranslationMaxLimit.Z);
                    con.LinearUpperLimit = new EV3(jt.TranslationMaxLimit.X, jt.TranslationMaxLimit.Y, -jt.TranslationMinLimit.Z);
                    // 旋转限制
                    // x/y轴镜像取反交换，z不变（与IK角度限制的镜像规则一致）
                    con.AngularLowerLimit = new EV3(-jt.RotationRadianMaxLimit.X, -jt.RotationRadianMaxLimit.Y, jt.RotationRadianMinLimit.Z);
                    con.AngularUpperLimit = new EV3(-jt.RotationRadianMinLimit.X, -jt.RotationRadianMinLimit.Y, jt.RotationRadianMaxLimit.Z);
                    // 弹簧平移仅非零时启用，旋转三轴恒启用（babylon-mmd）；刚度随质量同比缩放保持加速度不变
                    if (jt.TranslationSpring.X != 0f) { con.SetStiffness(0, jt.TranslationSpring.X * mass_scale); con.EnableSpring(0, true); }
                    if (jt.TranslationSpring.Y != 0f) { con.SetStiffness(1, jt.TranslationSpring.Y * mass_scale); con.EnableSpring(1, true); }
                    if (jt.TranslationSpring.Z != 0f) { con.SetStiffness(2, jt.TranslationSpring.Z * mass_scale); con.EnableSpring(2, true); }
                    con.SetStiffness(3, jt.RotationSpring.X * mass_scale); con.EnableSpring(3, true);
                    con.SetStiffness(4, jt.RotationSpring.Y * mass_scale); con.EnableSpring(4, true);
                    con.SetStiffness(5, jt.RotationSpring.Z * mass_scale); con.EnableSpring(5, true);
                    phys_world.AddConstraint(con, false); // 碰撞过滤完全交给碰撞组，不禁用相连刚体间碰撞（MMD行为）
                    phys_constraints.Add(con);
                    // katwat修正
                    // 物理刚体A与物理+骨位置刚体B相连且B骨的父骨正是A骨时，B降级为纯物理，避免反馈振荡
                    if (phys_body_mode[a] != 0 && phys_body_mode[b] == 2
                        && phys_body_bone[a] >= 0 && phys_body_bone[b] >= 0
                        && bone_parent[phys_body_bone[b]] == phys_body_bone[a])
                    { phys_body_mode[b] = 1; }
                    else if (phys_body_mode[b] != 0 && phys_body_mode[a] == 2
                        && phys_body_bone[a] >= 0 && phys_body_bone[b] >= 0
                        && bone_parent[phys_body_bone[a]] == phys_body_bone[b])
                    { phys_body_mode[a] = 1; }
                }
                phys_needs_reset = true;
            }
            catch (Exception ex)
            {
                destroy_physics();
                MessageBox.Show($"初始化物理引擎失败，物理模拟将不可用。原因：{ex.Message}\n请把上述的错误原因报告给开发者以解决对应问题。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private void destroy_physics() // 销毁物理世界及全部原生资源
        {
            if (phys_world != null)
            {
                for (int i = 0; i < phys_constraints.Count; i++)
                {
                    phys_world.RemoveConstraint(phys_constraints[i]);
                    phys_constraints[i].Dispose();
                }
                for (int i = 0; i < phys_bodies.Count; i++)
                {
                    phys_world.RemoveRigidBody(phys_bodies[i]);
                    if (phys_bodies[i].MotionState != null) { phys_bodies[i].MotionState.Dispose(); }
                    phys_bodies[i].Dispose();
                }
                if (phys_ground != null)
                {
                    phys_world.RemoveRigidBody(phys_ground);
                    phys_ground.Dispose();
                    phys_ground = null;
                }
                phys_world.Dispose();
                phys_world = null;
            }
            phys_constraints.Clear();
            phys_bodies.Clear();
            for (int i = 0; i < phys_shapes.Count; i++) { phys_shapes[i].Dispose(); }
            phys_shapes.Clear();
            if (phys_solver != null) { phys_solver.Dispose(); phys_solver = null; }
            if (phys_broadphase != null) { phys_broadphase.Dispose(); phys_broadphase = null; }
            if (phys_dispatcher != null) { phys_dispatcher.Dispose(); phys_dispatcher = null; }
            if (phys_config != null) { phys_config.Dispose(); phys_config = null; }
            phys_body_bone = Array.Empty<int>();
            phys_body_mode = Array.Empty<int>();
            phys_weld_a.Clear();
            phys_weld_b.Clear();
            phys_weld_rel.Clear();
            phys_body_offset = Array.Empty<OM4>();
            phys_body_offset_inv = Array.Empty<OM4>();
            phys_body_initial = Array.Empty<OM4>();
            phys_bone_world = Array.Empty<OM4>();
            phys_bone_flag = Array.Empty<byte>();
        }
        private void step_physics(double gap_ms) // 每帧物理管线
        // 同步骨骼、步进模拟、写回骨骼（在compute_bone_transforms之后调用）
        {
            if (phys_world == null || animated_world_buf.Length == 0) { return; }
            float dt = (float)(gap_ms / 1000.0);
            if (phys_needs_reset || gap_ms > suspend_gap_ms || dt <= 0f)
            {
                physics_reset_bodies();
                phys_needs_reset = false;
                dt = 1f / 60f;
            }
            physics_sync_bodies();
            phys_world.StepSimulation(dt, 10, 1f / 120f); // 固定步长1/120s，最多10子步（saba）
            for (int wi = 0; wi < phys_weld_a.Count; wi++)
            {
                OM4 wa_world = em4_to_om4(phys_bodies[phys_weld_a[wi]].WorldTransform);
                OM4 wb_world = phys_weld_rel[wi] * wa_world;
                phys_bodies[phys_weld_b[wi]].WorldTransform = om4_to_em4(wb_world);
                phys_bodies[phys_weld_b[wi]].LinearVelocity = phys_bodies[phys_weld_a[wi]].LinearVelocity;
                phys_bodies[phys_weld_b[wi]].AngularVelocity = phys_bodies[phys_weld_a[wi]].AngularVelocity;
            }
            physics_sync_bones();
        }
        private void physics_reset_bodies()
        // 全部刚体对齐到当前骨骼位姿，速度/受力清零
        {
            for (int i = 0; i < phys_bodies.Count; i++)
            {
                int bone = phys_body_bone[i];
                OM4 target = bone >= 0 ? phys_body_offset[i] * animated_world_buf[bone] : phys_body_initial[i];
                EM4 em = om4_to_em4(target);
                phys_bodies[i].WorldTransform = em;
                if (phys_bodies[i].MotionState != null) { phys_bodies[i].MotionState.WorldTransform = em; }
                phys_bodies[i].LinearVelocity = new EV3(0f, 0f, 0f);
                phys_bodies[i].AngularVelocity = new EV3(0f, 0f, 0f);
                phys_bodies[i].ClearForces();
            }
        }
        private void physics_sync_bodies() // 跟随骨骼的运动学刚体同步到骨骼世界位姿
        {
            for (int i = 0; i < phys_bodies.Count; i++)
            {
                int bone = phys_body_bone[i];
                if (bone < 0) { continue; }
                if (phys_body_mode[i] == 0)
                {
                    EM4 em = om4_to_em4(phys_body_offset[i] * animated_world_buf[bone]);
                    phys_bodies[i].WorldTransform = em;
                    if (phys_bodies[i].MotionState != null) { phys_bodies[i].MotionState.WorldTransform = em; }
                }
                else if (phys_body_mode[i] == 2)
                {
                    // 位置拉回骨骼处防漂移，旋转保留物理结果（three.js）
                    OM4 target = phys_body_offset[i] * animated_world_buf[bone];
                    EM4 cur = phys_bodies[i].WorldTransform;
                    cur.M41 = target.M41; cur.M42 = target.M42; cur.M43 = target.M43;
                    phys_bodies[i].WorldTransform = cur;
                }
            }
        }
        private void physics_sync_bones() // 动力学刚体的位姿写回骨骼，并按拓扑序重新传播全部骨骼世界矩阵/蒙皮矩阵
        {
            int n = bone_skinning.Length;
            if (phys_bone_flag.Length != n) { return; }
            Array.Clear(phys_bone_flag, 0, n);
            for (int i = 0; i < phys_bodies.Count; i++)
            {
                int bone = phys_body_bone[i];
                if (bone < 0 || phys_body_mode[i] == 0) { continue; }
                OM4 body_world = em4_to_om4(phys_bodies[i].WorldTransform);
                phys_bone_world[bone] = phys_body_offset_inv[i] * body_world;
                phys_bone_flag[bone] = phys_body_mode[i] == 1 ? (byte)1 : (byte)2;
            }
            for (int ti = 0; ti < n; ti++)
            {
                int i = bone_topology[ti];
                if (phys_bone_flag[i] == 1)
                { animated_world_buf[i] = phys_bone_world[i]; } // 世界矩阵直接用物理结果
                else
                {
                    int p = bone_parent[i];
                    OM4 world = p < 0 ? animated_local_buf[i] : animated_local_buf[i] * animated_world_buf[p];
                    if (phys_bone_flag[i] == 2)
                    {
                        // 物理+骨位置
                        // 旋转取物理，平移取骨骼层级传播结果
                        OM4 pw = phys_bone_world[i];
                        pw.M41 = world.M41; pw.M42 = world.M42; pw.M43 = world.M43;
                        world = pw;
                    }
                    animated_world_buf[i] = world; // 非物理骨（含物理后变形骨）基于物理后的父骨世界重新传播
                }
                bone_skinning[i] = bone_inv_rest[i] * animated_world_buf[i];
            }
        }

        private static int compare_frame(bone_keyframe a, bone_keyframe b)
        {
            return a.frame.CompareTo(b.frame);
        }
  
        private static int upper_bound_frame<T>(List<T> frames, float frame, Func<T, int> get_frame) // 二分查找第一个帧号严格大于frame的下标（列表须按帧号升序，全部小于时返回列表长度）
        {
            int lo = 0, hi = frames.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (get_frame(frames[mid]) <= frame) { lo = mid + 1; }
                else { hi = mid; }
            }
            return lo;
        }
        private static int frame_of_bone_keyframe(bone_keyframe kf) { return kf.frame; }
        private static int frame_of_morph_keyframe(morph_keyframe kf) { return kf.frame; }
        private static int frame_of_camera_keyframe(camera_keyframe kf) { return kf.frame; }
        private static int frame_of_ik_state((int frame, bool enabled) st) { return st.frame; }

        private static OQ append_scale_quat(OQ q, float ratio) // 继承比例缩放
        {
            // ratio == 1直取，== -1取逆，分数用Slerp(Identity,q,|ratio|)后按符号取逆
            if (ratio == 1f) { return q; }
            if (ratio == -1f) { return q.Inverted(); }
            OQ r = OQ.Slerp(OQ.Identity, q, MathF.Abs(ratio));
            if (ratio < 0f) { r = r.Inverted(); }
            return r;
        }

        private void solve_one_ik(ik_solver_info solver) // CCD迭代IK解算，在变形顺序中到达该IK骨时调用（单个solver）
        {
            ReadOnlySpan<Bone> bones = model!.BoneList.Span;
            // 检查IK是否启用（来自VMD的IK状态关键帧）
            string ik_name = bones[solver.ik_bone].Name;
            bool ik_on = is_ik_enabled(ik_name);
            if (!ik_on) { return; }

            OV3 ik_pos = animated_world_buf[solver.ik_bone].Row3.Xyz;
            OV3 target_pos = animated_world_buf[solver.target_bone].Row3.Xyz;
            if ((ik_pos - target_pos).LengthSquared < 1e-8f) { return; }

            int half_iter = solver.iterations >> 1;
            bool converged = false;
            for (int iter = 0; iter < solver.iterations; iter++)
            {
                bool use_axis = iter < half_iter;
                for (int ci = 0; ci < solver.chains.Count; ci++)
                {
                    ik_chain_info chain = solver.chains[ci];
                    if (chain.solve_axis == 4) { continue; } // Fixed轴跳过

                    OV3 chain_pos = animated_world_buf[chain.bone].Row3.Xyz;
                    OV3 chain_target_vec = chain_pos - target_pos;
                    OV3 chain_ik_vec = chain_pos - ik_pos;
                    float len_t = chain_target_vec.Length;
                    float len_i = chain_ik_vec.Length;
                    if (len_t < 1e-8f || len_i < 1e-8f) { continue; }
                    chain_target_vec /= len_t;
                    chain_ik_vec /= len_i;

                    OV3 rot_axis = OV3.Cross(chain_target_vec, chain_ik_vec);
                    if (rot_axis.LengthSquared < 1e-8f) { continue; }

                    float dot = Math.Clamp(OV3.Dot(chain_target_vec, chain_ik_vec), -1f, 1f);
                    float angle = MathF.Acos(dot);
                    float cap = solver.limit_angle * (ci + 1);
                    if (angle > cap) { angle = cap; }

                    // 将旋转轴转换到父骨骼局部空间
                    OV3 local_axis;
                    bool axis_limited = false;
                    int p = bone_parent[chain.bone];
                    if (p >= 0)
                    {
                        OM4 pw = animated_world_buf[p];
                        pw.Row3 = new OV4(0, 0, 0, 1);
                        if (chain.has_limit && use_axis)
                        {
                            if (chain.solve_axis == 1) // X轴
                            {
                                OV3 px = pw.Row0.Xyz;
                                local_axis = new OV3(OV3.Dot(rot_axis, px) >= 0 ? 1f : -1f, 0, 0);
                                axis_limited = true;
                            }
                            else if (chain.solve_axis == 2) // Y轴
                            {
                                OV3 py = pw.Row1.Xyz;
                                local_axis = new OV3(0, OV3.Dot(rot_axis, py) >= 0 ? 1f : -1f, 0);
                                axis_limited = true;
                            }
                            else if (chain.solve_axis == 3) // Z轴
                            {
                                OV3 pz = pw.Row2.Xyz;
                                local_axis = new OV3(0, 0, OV3.Dot(rot_axis, pz) >= 0 ? 1f : -1f);
                                axis_limited = true;
                            }
                            else { local_axis = OV3.TransformNormalInverse(rot_axis, pw); } // 将世界空间旋转轴转到父骨骼局部空间
                        }
                        else { local_axis = OV3.TransformNormalInverse(rot_axis, pw); } // 将世界空间旋转轴转到父骨骼局部空间
                    }
                    else { local_axis = rot_axis; }
                    if (!axis_limited) { local_axis = OV3.Normalize(local_axis); }
                    OQ q_corr = OQ.FromAxisAngle(local_axis, angle);
                    OQ q_new = q_corr * local_rot_buf[chain.bone];

                    // 将合成后的局部旋转转为欧拉角并钳位
                    if (chain.has_limit) { q_new = clamp_euler(chain, q_new, use_axis); }
                    local_rot_buf[chain.bone] = q_new;
                    // 更新受影响的骨骼世界矩阵（沿update_path传播）
                    update_ik_path(solver, ci);
                    target_pos = animated_world_buf[solver.target_bone].Row3.Xyz;
                    if ((ik_pos - target_pos).LengthSquared < 1e-8f)
                    {
                        converged = true;
                        break; 
                    }
                }
                if (converged) { break; }
            }
        }
        private bool is_ik_enabled(string bone_name)
        {
            if (current_motion == null) { return true; }
            if (!current_motion.ik_state_map.TryGetValue(bone_name, out List<(int frame, bool enabled)>? states)) { return true; }
            if (states.Count == 0) { return true; }
            // 二分查找最后一个 ≤ current_frame 的状态（第一个大于的下标减一）
            int ub = upper_bound_frame(states, (int)current_frame, frame_of_ik_state);
            if (ub <= 0) { return true; } // 所有状态都在未来，默认启用
            return states[ub - 1].enabled;
        }

        private void update_ik_path(ik_solver_info solver, int chain_index)
        {
            int path_start = solver.path_position[chain_index];
            if (path_start < 0) { return; }
            int cb = solver.update_path[path_start];
            // 重建该骨骼的局部矩阵（位置不变，旋转更新）
            OQ q = local_rot_buf[cb];
            OV3 pos = animated_local_buf[cb].Row3.Xyz;
            OM4 lm = OM4.CreateFromQuaternion(q) * OM4.CreateTranslation(pos);
            animated_local_buf[cb] = lm;
            // 沿路径向下传播世界变换
            for (int pi = path_start; pi < solver.update_path.Length; pi++)
            {
                int bi = solver.update_path[pi];
                int parent = (pi > 0) ? solver.update_path[pi - 1] : bone_parent[bi];
                if (parent < 0) { animated_world_buf[bi] = animated_local_buf[bi]; }
                else { animated_world_buf[bi] = animated_local_buf[bi] * animated_world_buf[parent]; }
            }
        }

        private static OQ clamp_euler(ik_chain_info chain, OQ q, bool use_axis) // 欧拉分解与钳制
        {
            OM4 m = OM4.CreateFromQuaternion(q);
            const float threshold = 88f * MathF.PI / 180f; // babylon-mmd
            float rx = 0, ry = 0, rz = 0;
            if (chain.rotation_order == 0) // YXZ
            {
                rx = MathF.Asin(Math.Clamp(-m.M32, -1f, 1f));
                if (MathF.Abs(rx) > threshold) { rx = rx < 0 ? -threshold : threshold; }
                ry = MathF.Atan2(m.M31, m.M33);
                rz = MathF.Atan2(m.M12, m.M22);
                rx = limit_angle(rx, chain.min_angle.X, chain.max_angle.X, use_axis);
                ry = limit_angle(ry, chain.min_angle.Y, chain.max_angle.Y, use_axis);
                rz = limit_angle(rz, chain.min_angle.Z, chain.max_angle.Z, use_axis);
                return OQ.FromAxisAngle(OV3.UnitY, ry) * OQ.FromAxisAngle(OV3.UnitX, rx) * OQ.FromAxisAngle(OV3.UnitZ, rz);
            }
            if (chain.rotation_order == 1) // ZYX
            {
                ry = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
                if (MathF.Abs(ry) > threshold) { ry = ry < 0 ? -threshold : threshold; }
                rx = MathF.Atan2(m.M23, m.M33);
                rz = MathF.Atan2(m.M12, m.M11);
                rx = limit_angle(rx, chain.min_angle.X, chain.max_angle.X, use_axis);
                ry = limit_angle(ry, chain.min_angle.Y, chain.max_angle.Y, use_axis);
                rz = limit_angle(rz, chain.min_angle.Z, chain.max_angle.Z, use_axis);
                return OQ.FromAxisAngle(OV3.UnitZ, rz) * OQ.FromAxisAngle(OV3.UnitY, ry) * OQ.FromAxisAngle(OV3.UnitX, rx);
            }
            // XZY
            rz = MathF.Asin(Math.Clamp(-m.M21, -1f, 1f));
            if (MathF.Abs(rz) > threshold) { rz = rz < 0 ? -threshold : threshold; }
            rx = MathF.Atan2(m.M23, m.M22);
            ry = MathF.Atan2(m.M31, m.M11);
            rx = limit_angle(rx, chain.min_angle.X, chain.max_angle.X, use_axis);
            ry = limit_angle(ry, chain.min_angle.Y, chain.max_angle.Y, use_axis);
            rz = limit_angle(rz, chain.min_angle.Z, chain.max_angle.Z, use_axis);
            return OQ.FromAxisAngle(OV3.UnitX, rx) * OQ.FromAxisAngle(OV3.UnitZ, rz) * OQ.FromAxisAngle(OV3.UnitY, ry);
        }

        private static float limit_angle(float angle, float min, float max, bool use_axis)
        {
            if (angle < min)
            {
                float diff = 2f * min - angle;
                return (diff <= max && use_axis) ? diff : min;
            }
            if (angle > max)
            {
                float diff = 2f * max - angle;
                return (diff >= min && use_axis) ? diff : max;
            }
            return angle;
        }

        private void compute_bone_transforms()
        {
            if (model == null || current_motion == null) { return; }
            ReadOnlySpan<Bone> bones = model.BoneList.Span;
            int n = bones.Length;

            // FK逐骨骼插值，同时保存局部旋转四元数供IK使用
            for (int i = 0; i < n; i++)
            {
                OQ qconv = OQ.Identity;
                OV3 t = OV3.Zero;
                string name = bones[i].Name;
                string name_en = bones[i].NameEnglish;

                if (interpolate_bone(name, current_frame, out t, out qconv)
                    || (!string.IsNullOrEmpty(name_en) && interpolate_bone(name_en, current_frame, out t, out qconv))) { t.Z = -t.Z; }
                // PMX转OpenGL镜像
                // 四元数(x,y,z,w)变(-x,-y,z,w)
                qconv = new OQ(-qconv.X, -qconv.Y, qconv.Z, qconv.W);

                MV3 world_p = bones[i].Position;
                OV3 local = new OV3(world_p.X, world_p.Y, -world_p.Z);
                if (bone_parent[i] >= 0)
                {
                    MV3 parent_w = bones[bone_parent[i]].Position;
                    local -= new OV3(parent_w.X, parent_w.Y, -parent_w.Z);
                }
                OV3 anim_off = movable_bones.Contains(name) ? t : OV3.Zero;
                bone_anim_offset[i] = anim_off;   // 记录动画位移偏移，供继承位移复制
                OV3 pos = local + anim_off;

                animated_local_buf[i] = OM4.CreateFromQuaternion(qconv) * OM4.CreateTranslation(pos);
                local_rot_buf[i] = qconv;
            }
            apply_bone_morphs(n); // 将morph位移/旋转叠加到FK结果
            // 变形顺序(afterPhysics,TransformDepth,index)保证继承源先于继承目标、IK先于其D骨求解
            for (int oi = 0; oi < n; oi++)
            {
                int i = deform_order[oi];
                // 继承源从源骨复制局部旋转/位移并按率混合（源在变形顺序中先处理，已含其FK+IK+继承结果）
                if (bone_append_source[i] >= 0)
                {
                    int src = bone_append_source[i];
                    float ratio = bone_append_ratio[i];
                    if (bone_append_rot[i])
                    {
                        OQ appended = append_scale_quat(local_rot_buf[src], ratio);
                        local_rot_buf[i] = local_rot_buf[i] * appended;
                    }
                    if (bone_append_pos[i]) { bone_anim_offset[i] += bone_anim_offset[src] * ratio; }
                }
                // 重建局部矩阵（旋转 = local_rot_buf, 位置 = rest局部 + 动画偏移）
                OV3 lpos = bone_rest_local[i].Row3.Xyz + bone_anim_offset[i];
                OM4 ml = OM4.CreateFromQuaternion(local_rot_buf[i]) * OM4.CreateTranslation(lpos);
                animated_local_buf[i] = ml;
                // 世界矩阵
                int p = bone_parent[i];
                if (p < 0) { animated_world_buf[i] = ml; }
                else { animated_world_buf[i] = ml * animated_world_buf[p]; }
                if (bone_ik_solver[i] != null) { solve_one_ik(bone_ik_solver[i]!); } // 该骨为IK骨则触发CCD求解（回写链骨local_rot_buf并沿路径更新世界矩阵）
            }

            // 最终全量传播（IK可能修改了local_rot_buf及局部矩阵，需对非路径骨骼重算世界、蒙皮）
            for (int ti = 0; ti < n; ti++)
            {
                int i = bone_topology[ti];
                // 重建局部矩阵（位置保留，旋转用local_rot_buf的最新值）
                OQ q = local_rot_buf[i];
                OV3 lpos = animated_local_buf[i].Row3.Xyz;
                OM4 ml = OM4.CreateFromQuaternion(q) * OM4.CreateTranslation(lpos);
                animated_local_buf[i] = ml;
                int p = bone_parent[i];
                if (p < 0) { animated_world_buf[i] = ml; }
                else { animated_world_buf[i] = ml * animated_world_buf[p]; }
                bone_skinning[i] = bone_inv_rest[i] * animated_world_buf[i];
            }
        }

        private bool interpolate_bone(string name, float frame, out OV3 pos, out OQ rot)
        {
            pos = OV3.Zero;
            rot = OQ.Identity;
            if (!bone_frame_map.TryGetValue(name, out List<bone_keyframe>? frames))
            { return false; }
            int right = upper_bound_frame(frames, frame, frame_of_bone_keyframe);
            int left = right - 1;
            if (left < 0)
            {
                pos = frames[right].position;
                rot = frames[right].rotation;
                return true;
            }
            if (right >= frames.Count || frames[left].frame == frame)
            {
                pos = frames[left].position;
                rot = frames[left].rotation;
                return true;
            }
            float denom = frames[right].frame - frames[left].frame;
            if (denom < 0.001f)
            {
                pos = frames[left].position;
                rot = frames[left].rotation;
                return true;
            }
            float t = (frame - frames[left].frame) / denom;
            // 使用左关键帧的贝塞尔插值参数
            // 4轴×(x1,x2,y1,y2)
            bezier_curve[] ip = frames[left].interp;
            float fx = bezier_eval(ip[0].x1, ip[0].x2, ip[0].y1, ip[0].y2, t);
            float fy = bezier_eval(ip[1].x1, ip[1].x2, ip[1].y1, ip[1].y2, t);
            float fz = bezier_eval(ip[2].x1, ip[2].x2, ip[2].y1, ip[2].y2, t);
            float fr = bezier_eval(ip[3].x1, ip[3].x2, ip[3].y1, ip[3].y2, t);
            pos.X = frames[left].position.X + (frames[right].position.X - frames[left].position.X) * fx;
            pos.Y = frames[left].position.Y + (frames[right].position.Y - frames[left].position.Y) * fy;
            pos.Z = frames[left].position.Z + (frames[right].position.Z - frames[left].position.Z) * fz;
            rot = OQ.Slerp(frames[left].rotation, frames[right].rotation, fr);
            return true;
        }
        private static float bezier_eval(float x1, float x2, float y1, float y2, float x) // 三次贝塞尔插值
        {
            // 控制点(0,0)-(x1,y1)-(x2,y2)-(1,1)，二分法求解By(t)（babylon-mmd）
            float c = 0.5f, t = c, s = 1f - t;
            for (int i = 0; i < 15; i++)
            {
                float sst3 = 3f * s * s * t;
                float stt3 = 3f * s * t * t;
                float ttt = t * t * t;
                float ft = sst3 * x1 + stt3 * x2 + ttt - x;
                if (MathF.Abs(ft) < 1e-5f) { break; }
                c *= 0.5f;
                t += ft < 0 ? c : -c;
                s = 1f - t;
            }
            return 3f * s * s * t * y1 + 3f * s * t * t * y2 + t * t * t;
        }

        private void draw_material(int first, int count, Material mat, int mat_index)
        {
            if ((mat.DrawFlag & MaterialDrawFlag.BothSidesDrawing) != 0) { GL.Disable(EnableCap.CullFace); }
            else { GL.Enable(EnableCap.CullFace); }
            // 材质morph在基础颜色上套用本帧乘算/加算累加器（颜色钳制到[0,1]，shininess不小于0）
            float dr = mat.Diffuse.R, dg = mat.Diffuse.G, db = mat.Diffuse.B, da = mat.Diffuse.A;
            float spr = mat.Specular.R, spg = mat.Specular.G, spb = mat.Specular.B;
            float shn = mat.Shininess;
            float ar = mat.Ambient.R, ag = mat.Ambient.G, ab = mat.Ambient.B;
            if (matm_active && mat_index >= 0 && mat_index < matm_diffuse_mul.Length)
            {
                dr = Math.Clamp(dr * matm_diffuse_mul[mat_index].X + matm_diffuse_add[mat_index].X, 0f, 1f);
                dg = Math.Clamp(dg * matm_diffuse_mul[mat_index].Y + matm_diffuse_add[mat_index].Y, 0f, 1f);
                db = Math.Clamp(db * matm_diffuse_mul[mat_index].Z + matm_diffuse_add[mat_index].Z, 0f, 1f);
                da = Math.Clamp(da * matm_diffuse_mul[mat_index].W + matm_diffuse_add[mat_index].W, 0f, 1f);
                spr = Math.Clamp(spr * matm_specular_mul[mat_index].X + matm_specular_add[mat_index].X, 0f, 1f);
                spg = Math.Clamp(spg * matm_specular_mul[mat_index].Y + matm_specular_add[mat_index].Y, 0f, 1f);
                spb = Math.Clamp(spb * matm_specular_mul[mat_index].Z + matm_specular_add[mat_index].Z, 0f, 1f);
                shn = MathF.Max(0f, shn * matm_shininess_mul[mat_index] + matm_shininess_add[mat_index]);
                ar = Math.Clamp(ar * matm_ambient_mul[mat_index].X + matm_ambient_add[mat_index].X, 0f, 1f);
                ag = Math.Clamp(ag * matm_ambient_mul[mat_index].Y + matm_ambient_add[mat_index].Y, 0f, 1f);
                ab = Math.Clamp(ab * matm_ambient_mul[mat_index].Z + matm_ambient_add[mat_index].Z, 0f, 1f);
            }
            GL.Uniform4(uloc_obj_color, dr, dg, db, da);
            GL.Uniform3(uloc_spec_color, spr, spg, spb);
            GL.Uniform1(uloc_shininess, shn);
            int sid = default_texture;
            int sphere_mode = (int)mat.SphereTextureMode; // 0 = 无, 1 = 乘算, 2 = 加算, 3 = 按纹理alpha混合
            if (mat.SphereTextre >= 0 && textures.TryGetValue(mat.SphereTextre, out int st) && st > 0) { sid = st; }
            else { sphere_mode = 0; } // 球体贴图缺失/加载失败时禁用该模式，避免默认白纹理导致整块材质发白
            GL.Uniform1(uloc_sphere_mode, sphere_mode);
            GL.Uniform3(uloc_mat_ambient, ar, ag, ab);
            int tid = default_texture;
            bool has_main_tex = false;
            if (mat.Texture >= 0 && textures.TryGetValue(mat.Texture, out int t) && t > 0) { tid = t; has_main_tex = true; }
            GL.Uniform1(uloc_has_main_tex, has_main_tex ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, tid);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, sid);
            int toon_tex = default_toon_texture;
            bool has_toon = false;
            if (toon_textures.TryGetValue(mat_index, out int tt) && tt > 0)
            {
                toon_tex = tt;
                has_toon = true;
            }
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, toon_tex);
            GL.Uniform1(uloc_has_toon, has_toon ? 1 : 0);
            GL.DrawElements(BeginMode.Triangles, count, DrawElementsType.UnsignedInt, first);
        }
        private void cache_uniform_locations()
        {
            uloc_mvp = GL.GetUniformLocation(shader_program, "mvp");
            uloc_model = GL.GetUniformLocation(shader_program, "model");
            uloc_view = GL.GetUniformLocation(shader_program, "view");
            uloc_light_pos = GL.GetUniformLocation(shader_program, "lightPos");
            uloc_light_color = GL.GetUniformLocation(shader_program, "lightColor");
            uloc_view_pos = GL.GetUniformLocation(shader_program, "viewPos");
            uloc_tex_sampler = GL.GetUniformLocation(shader_program, "texSampler");
            uloc_obj_color = GL.GetUniformLocation(shader_program, "objColor");
            uloc_spec_color = GL.GetUniformLocation(shader_program, "specColor");
            uloc_shininess = GL.GetUniformLocation(shader_program, "shininess");
            uloc_sphere_mode = GL.GetUniformLocation(shader_program, "sphereMode");
            uloc_has_toon = GL.GetUniformLocation(shader_program, "hasToon");
            uloc_mat_ambient = GL.GetUniformLocation(shader_program, "matAmbient");
            uloc_use_lighting = GL.GetUniformLocation(shader_program, "useLighting");
            uloc_sphere_sampler = GL.GetUniformLocation(shader_program, "sphereSampler");
            uloc_toon_sampler = GL.GetUniformLocation(shader_program, "toonSampler");
            uloc_has_main_tex = GL.GetUniformLocation(shader_program, "hasMainTex");
            uloc_bone_tex = GL.GetUniformLocation(shader_program, "boneTex");
            uloc_bone_tex_width = GL.GetUniformLocation(shader_program, "boneTexWidth");
        }
        private static int compile_program(string vs, string fs)
        {
            int p = GL.CreateProgram();
            int v = compile_shader(ShaderType.VertexShader, vs);
            int f = compile_shader(ShaderType.FragmentShader, fs);
            GL.AttachShader(p, v); GL.AttachShader(p, f);
            GL.LinkProgram(p);
            GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0) { throw new Exception($"连接失败：{GL.GetProgramInfoLog(p)}"); }
            GL.DetachShader(p, v); GL.DeleteShader(v);
            GL.DetachShader(p, f); GL.DeleteShader(f);
            return p;
        }
        private static int compile_shader(ShaderType t, string src)
        {
            int s = GL.CreateShader(t);
            GL.ShaderSource(s, src);
            GL.CompileShader(s);
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0) { throw new Exception($"着色器编译失败！{t}: {GL.GetShaderInfoLog(s)}"); }
            return s;
        }

    }
    // 以下代码可以独立出来可供在别的地方用。
    // 贝塞尔插值曲线控制点（MMD插值曲线的4个控制点(0,0)-(x1,y1)-(x2,y2)-(1,1)，已归一化到[0,1]）
    internal struct bezier_curve
    {
        public float x1, x2, y1, y2;
    }
    // VMD动作数据类型
    internal class vmd_motion
    {
        public string model_name = ""; // 通常情况下没有什么用
        public List<bone_keyframe> bone_frames = new(); // 骨骼帧
        public int max_frame; // 最大帧数
        public List<ik_state> ik_states = new(); // IK状态
        public List<morph_keyframe> morph_frames = new(); // 变形（即MMD的表情）帧
        public List<camera_keyframe> camera_frames = new(); // 相机关键帧（按帧号排序）
        public Dictionary<string, List<(int frame, bool enabled)>> ik_state_map = new(); // 按帧号排序的IK启用/禁用状态列表（帧号，是否启用）
        public Dictionary<string, List<morph_keyframe>> morph_frame_map = new(); // 按帧号排序的关键帧列表
    }
    // 骨骼关键帧
    internal class bone_keyframe
    {
        public string bone_name = "";
        public int frame;
        public OV3 position;
        public OQ rotation;
        public bezier_curve[] interp = new bezier_curve[4]; // 贝塞尔插值（MMD的插值曲线点）。4轴×(x1,x2,y1,y2)，已归一化(/127)
    }

    internal class ik_state
    {
        public int frame;
        public string bone_name = "";
        public bool enabled;
    }
    // 变形关键帧
    internal class morph_keyframe
    {
        public string morph_name = ""; // 表情名字
        public int frame;              // 帧号
        public float weight;           // 权重
    }

    // VMD相机关键帧（MMD原始坐标系，坐标转换在求值时进行）
    internal class camera_keyframe
    {
        public int frame;            // 帧号
        public float distance;       // 到注视点距离（通常为负）
        public OV3 position;         // 注视点
        public OV3 rotation;         // 欧拉角（弧度。x = 俯仰、y = 偏航、z = 滚转）
        public bezier_curve[] interp = new bezier_curve[6]; // 贝塞尔插值参数（即MMD插值曲线）。6通道(X/Y/Z/旋转/距离/FOV)×(x1,x2,y1,y2)，已归一化(/127)
        public float fov;            // 视场角（度）
    }

    // CCD IK解算器预计算数据
    internal class ik_solver_info
    {
        public int ik_bone;          // IK骨骼索引（如左足IK）
        public int target_bone;      // 目标骨骼索引（如左足首）
        public int iterations;       // 最大迭代次数（限制≤256）
        public float limit_angle;    // 每次迭代最大转角
        public List<ik_chain_info> chains = new();   // 链骨骼（从目标端到根部）
        public int[] update_path = Array.Empty<int>();       // 从顶部链骨骼到目标的完整祖先路径
        public int[] path_position = Array.Empty<int>();     // 每根链骨骼在update_path中的位置
    }

    internal class ik_chain_info
    {
        public int bone;             // 该链骨骼的索引
        public bool has_limit;       // 是否有角度限制
        public OV3 min_angle;        // 角度下限（GL空间，弧度）
        public OV3 max_angle;        // 角度上限（GL空间，弧度）
        public int rotation_order;   // 欧拉角分解顺序（0 = YXZ、1 = ZYX、2 = XZY）
        public int solve_axis;       // 限制轴（0 = None、1 = X、2 = Y、3 = Z、4 = Fixed（固定跳过））
    }

    // VMD解析器
    internal static class vmd_parser
    {
        private static readonly Encoding shift_jis; // 需要注册日文编码器防止不兼容

        static vmd_parser() // 注册编码器
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            shift_jis = Encoding.GetEncoding("shift_jis");
        }

        public static vmd_motion parse(string file_path)
        {
            vmd_motion motion = new vmd_motion();
            motion.bone_frames = new List<bone_keyframe>();
            using (FileStream fs = new FileStream(file_path, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                string signature = read_string(reader, 30);
                bool is_v2 = signature.StartsWith("Vocaloid Motion Data 0002");
                bool is_v1 = signature.StartsWith("Vocaloid Motion Data file");
                if (!is_v2 && !is_v1) { throw new InvalidDataException("VMD文件无效"); }
                int name_len = is_v1 ? 10 : 20;
                motion.model_name = read_string(reader, name_len);

                int bone_count = reader.ReadInt32();
                for (int i = 0; i < bone_count; i++)
                {
                    bone_keyframe kf = new bone_keyframe();
                    kf.bone_name = read_string(reader, 15);
                    kf.frame = reader.ReadInt32();
                    kf.position = new OV3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    kf.rotation = new OQ(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    byte[] ip_raw = reader.ReadBytes(64); // 读取64字节贝塞尔插值数据
                    // 4轴×(x1, x2, y1, y2)，x1 = ip[axis*16+0], x2 = ip[axis*16+8], y1 = ip[axis*16+4], y2 = ip[axis*16+12]
                    // interpolation[axis*16 + {0:x1,4:y1,8:x2,12:y2}]
                    // 以上算法来自babylon-mmd
                    for (int a = 0; a < 4; a++)
                    {
                        int bo = a * 16;
                        kf.interp[a].x1 = ip_raw[bo + 0] / 127f; // x1
                        kf.interp[a].x2 = ip_raw[bo + 8] / 127f; // x2
                        kf.interp[a].y1 = ip_raw[bo + 4] / 127f; // y1
                        kf.interp[a].y2 = ip_raw[bo + 12] / 127f; // y2
                    }
                    motion.bone_frames.Add(kf);
                    if (kf.frame > motion.max_frame) { motion.max_frame = kf.frame; }
                }

                // 解析morph关键帧
                if (fs.Position + 4 <= fs.Length)
                {
                    int morph_count = reader.ReadInt32();
                    for (int i = 0; i < morph_count; i++)
                    {
                        morph_keyframe mk = new morph_keyframe();
                        mk.morph_name = read_string(reader, 15);
                        mk.frame = reader.ReadInt32();
                        mk.weight = reader.ReadSingle();
                        motion.morph_frames.Add(mk);
                    }
                }
                // 解析相机关键帧（61字节/帧）
                if (fs.Position + 4 <= fs.Length)
                {
                    int camera_count = reader.ReadInt32();
                    for (int i = 0; i < camera_count; i++)
                    {
                        camera_keyframe ck = new camera_keyframe();
                        ck.frame = reader.ReadInt32();
                        ck.distance = reader.ReadSingle();
                        ck.position = new OV3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        ck.rotation = new OV3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        // 24字节插值，6通道连续存放，每通道(x1,x2,y1,y2)，与骨骼帧的stride布局不同
                        byte[] cam_ip = reader.ReadBytes(24);
                        for (int c = 0; c < 6; c++)
                        {
                            ck.interp[c].x1 = cam_ip[c * 4 + 0] / 127f;
                            ck.interp[c].x2 = cam_ip[c * 4 + 1] / 127f;
                            ck.interp[c].y1 = cam_ip[c * 4 + 2] / 127f;
                            ck.interp[c].y2 = cam_ip[c * 4 + 3] / 127f;
                        }
                        ck.fov = reader.ReadInt32();
                        reader.ReadByte(); // 透视标志（正交模式本程序不支持，跳过）
                        motion.camera_frames.Add(ck);
                        if (ck.frame > motion.max_frame) { motion.max_frame = ck.frame; }
                    }
                }
                if (fs.Position + 4 <= fs.Length) { int light_count = reader.ReadInt32(); reader.ReadBytes(light_count * 28); }
                if (fs.Position + 4 <= fs.Length) { int shadow_count = reader.ReadInt32(); reader.ReadBytes(shadow_count * 9); }

                // 解析IK启用/禁用状态（模型显示段）
                if (fs.Position + 4 <= fs.Length)
                {
                    int model_count = reader.ReadInt32();
                    for (int i = 0; i < model_count; i++)
                    {
                        int ik_frame = reader.ReadInt32();
                        reader.ReadByte(); // visible
                        if (fs.Position + 4 > fs.Length) { break; }
                        int ik_count = reader.ReadInt32();
                        for (int j = 0; j < ik_count; j++)
                        {
                            string ik_name = read_string(reader, 20);
                            byte en = fs.Position < fs.Length ? reader.ReadByte() : (byte)1;
                            ik_state st = new ik_state { frame = ik_frame, bone_name = ik_name, enabled = en != 0 };
                            motion.ik_states.Add(st);
                        }
                    }
                }
            }
            motion.max_frame++;
            motion.camera_frames.Sort(compare_camera_frame); // 相机关键帧按帧号排序（插值依赖有序列表）
            // 构建IK状态查找表
            motion.ik_state_map = new Dictionary<string, List<(int frame, bool enabled)>>();
            foreach (ik_state st in motion.ik_states)
            {
                if (!motion.ik_state_map.TryGetValue(st.bone_name, out List<(int, bool)>? list))
                {
                    list = new List<(int, bool)>();
                    motion.ik_state_map[st.bone_name] = list;
                }
                list.Add((st.frame, st.enabled));
            }
            // 构建morph关键帧查找表
            motion.morph_frame_map = new Dictionary<string, List<morph_keyframe>>();
            foreach (morph_keyframe mk in motion.morph_frames)
            {
                if (!motion.morph_frame_map.TryGetValue(mk.morph_name, out List<morph_keyframe>? list))
                {
                    list = new List<morph_keyframe>();
                    motion.morph_frame_map[mk.morph_name] = list;
                }
                list.Add(mk);
            }
            
            foreach (List<morph_keyframe> list in motion.morph_frame_map.Values) { list.Sort(compare_morph_frame); } // morph关键帧按帧号排序（插值依赖有序列表）
            return motion;
        }

        private static int compare_morph_frame(morph_keyframe a, morph_keyframe b) 
        { 
            return a.frame.CompareTo(b.frame); 
        }

        private static int compare_camera_frame(camera_keyframe a, camera_keyframe b)
        {
            return a.frame.CompareTo(b.frame);
        }

        private static string read_string(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            int null_idx = Array.IndexOf(bytes, (byte)0); // 找到第一个\0的位置，未找到为-1
            if (null_idx == 0) { return ""; }
            if (null_idx > 0) { length = null_idx; }
            return shift_jis.GetString(bytes, 0, length);
        }
    }
}

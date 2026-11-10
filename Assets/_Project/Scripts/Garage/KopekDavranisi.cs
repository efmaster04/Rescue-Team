using UnityEngine;

namespace CompanySim
{
    // Hangarın köpeği (Fındık). Yatağında uyur; kalkıp serbest alanda gezinir, koklar, oturur, uzanır, mamasını yer.
    // Oyuncu yaklaşınca ona döner, kuyruğunu sallar, ara sıra bir kez havlar; uyurken çok yaklaşılırsa uyanır. E ile sevilir.
    // Animasyonlar yerinde oynar (Kopek.fbx, 16 klip, Animator durumları klip adlarıyla); yürüyüşü bu sınıf taşır.
    // Engelden kaçma yok: hedefler kurulum aracının boş bıraktığı dikdörtgen alandan seçilir. Çarpıştırıcı alt nesnede tetiktir
    // (FindTarget onu bulur, oyuncu köpeğe takılmaz).
    public sealed class KopekDavranisi : InteractableBehaviour
    {
        public string dogName = "Fındık";
        public Vector2 areaMin = new Vector2(13.4f, -1.4f), areaMax = new Vector2(17.2f, 2.9f);
        public Transform bed, bowl;
        public Animator animator;
        public float walkSpeed = 0.33f, trotSpeed = 0.8f, turnSpeed = 220f;
        public float noticeDistance = 3.2f, wakeDistance = 1.6f;

        enum State { Sleep, LieIdle, StandUp, Idle, Move, Sniff, SitDown, Sit, SitUp, LieDown, Lie, Eat, Happy, Bark }

        State state;
        float until, barkReady, petUntil;
        Vector3 target;
        State afterMove;
        bool moveToBed, moveToBowl;
        string playing = "";
        Transform player;
        CompanyRuntime runtime;

        public string CurrentClip => playing;
        public bool IsSleeping => state == State.Sleep;
        public int Pets { get; private set; }

        void Start()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (bed != null) { transform.SetPositionAndRotation(Flat(bed.position), Quaternion.Euler(0f, bed.eulerAngles.y, 0f)); }
            Enter(State.Sleep, Random.Range(12f, 30f));
            barkReady = Time.time + 5f;
        }

        void Update()
        {
            if (animator == null) return;
            if (player == null)
            {
                var p = FindAnyObjectByType<PlayerInteractor>();
                if (p != null) player = p.transform;
            }
            float d = player != null ? Vector3.Distance(Flat(player.position), Flat(transform.position)) : 99f;
            bool near = d < noticeDistance;

            switch (state)
            {
                case State.Sleep:
                    if (d < wakeDistance || Time.time > until) Enter(State.LieIdle, d < wakeDistance ? 0.6f : Random.Range(3f, 6f));
                    break;
                case State.LieIdle:
                    if (Time.time > until) Enter(State.StandUp, 1.05f);
                    break;
                case State.StandUp:
                case State.SitUp:
                    if (Time.time > until) Enter(near ? State.Happy : State.Idle, near ? 2.5f : Random.Range(1.5f, 3.5f));
                    break;
                case State.Idle:
                    if (near) { Enter(State.Happy, 2.5f); break; }
                    if (Time.time > until) ChooseNext();
                    break;
                case State.Happy:
                    Face(player != null ? player.position : transform.position + transform.forward);
                    if (near && barkReady < Time.time && Time.time > petUntil) { barkReady = Time.time + Random.Range(18f, 35f); Enter(State.Bark, 1.0f); break; }
                    if (near || Time.time < petUntil) until = Mathf.Max(until, Time.time + 1.5f);
                    if (Time.time > until) Enter(State.Idle, Random.Range(1f, 2.5f));
                    break;
                case State.Bark:
                    Face(player != null ? player.position : transform.position + transform.forward);
                    if (Time.time > until) Enter(State.Happy, 2f);
                    break;
                case State.Move:
                    if (near && !moveToBed) { Enter(State.Happy, 2.5f); break; }
                    Walk();
                    break;
                case State.Sniff:
                case State.Eat:
                    if (near && state == State.Sniff) { Enter(State.Happy, 2.5f); break; }
                    if (Time.time > until) Enter(State.Idle, Random.Range(1f, 2f));
                    break;
                case State.SitDown:
                    if (Time.time > until) Enter(State.Sit, Random.Range(6f, 12f));
                    break;
                case State.Sit:
                    if (near) Face(player.position);
                    if (Time.time > until) Enter(State.SitUp, 0.8f);
                    break;
                case State.LieDown:
                    if (Time.time > until) Enter(moveToBed ? State.Sleep : State.Lie, moveToBed ? Random.Range(25f, 50f) : Random.Range(6f, 10f));
                    break;
                case State.Lie:
                    if (near) { Enter(State.StandUp, 1.05f); break; }
                    if (Time.time > until) Enter(Random.value < 0.4f ? State.Sleep : State.StandUp, Random.Range(15f, 30f));
                    break;
            }
        }

        void ChooseNext()
        {
            moveToBed = moveToBowl = false;
            float r = Random.value;
            if (r < 0.42f) { GoTo(RandomPoint(), State.Idle); }
            else if (r < 0.56f) Enter(State.Sniff, Random.Range(2f, 4f));
            else if (r < 0.70f) Enter(State.SitDown, 1.0f);
            else if (r < 0.80f) Enter(State.LieDown, 1.33f);
            else if (r < 0.90f && bowl != null) { moveToBowl = true; GoTo(bowl.position + (transform.position - bowl.position).normalized * 0.35f, State.Eat); }
            else if (bed != null) { moveToBed = true; GoTo(bed.position, State.LieDown); }
            else Enter(State.Idle, 2f);
        }

        void GoTo(Vector3 p, State then)
        {
            target = Flat(p);
            afterMove = then;
            state = State.Move;
            Play(Vector3.Distance(Flat(transform.position), target) > 2.5f ? "Trot" : "Walk");
        }

        void Walk()
        {
            Vector3 to = target - Flat(transform.position);
            float dist = to.magnitude;
            if (dist < 0.12f)
            {
                if (moveToBed && bed != null) transform.rotation = Quaternion.Euler(0f, bed.eulerAngles.y, 0f);
                if (moveToBowl && bowl != null) Face(bowl.position);
                float dur = afterMove == State.Eat ? Random.Range(3f, 5f) : afterMove == State.LieDown ? 1.33f : Random.Range(1.5f, 3f);
                Enter(afterMove, dur);
                return;
            }
            Face(target);
            float angle = Vector3.Angle(transform.forward, to);
            float speed = playing == "Trot" ? trotSpeed : walkSpeed;
            if (angle < 60f) transform.position += transform.forward * Mathf.Min(dist, speed * Time.deltaTime) * Mathf.Clamp01(1.2f - angle / 60f);
        }

        void Face(Vector3 p)
        {
            Vector3 to = Flat(p) - Flat(transform.position);
            if (to.sqrMagnitude < 0.0004f) return;
            var want = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
        }

        void Enter(State s, float duration)
        {
            state = s;
            until = Time.time + duration;
            switch (s)
            {
                case State.Sleep: Play("Lie_Sleep"); break;
                case State.LieIdle: case State.Lie: Play("Lie_Idle"); break;
                case State.StandUp: Play("Lie_To_Stand"); break;
                case State.Idle: Play("Idle"); break;
                case State.Happy: Play("Idle_Happy"); break;
                case State.Bark: Play("Bark"); break;
                case State.Sniff: Play("Sniff"); break;
                case State.Eat: Play("Eat"); break;
                case State.SitDown: Play("Sit_Down"); break;
                case State.Sit: Play("Sit_Idle"); break;
                case State.SitUp: Play("Sit_To_Stand"); break;
                case State.LieDown: Play("Lie_Down"); break;
            }
        }

        void Play(string clip)
        {
            if (playing == clip) return;
            // yatarken uyuklamaya geçiş yavaş (0,5 sn), diğerleri 0,15 sn (kaynak README)
            float fade = (playing == "Lie_Idle" && clip == "Lie_Sleep") || (playing == "Lie_Sleep" && clip == "Lie_Idle") ? 0.5f : 0.15f;
            playing = clip;
            animator.CrossFadeInFixedTime(clip, fade);
        }

        Vector3 RandomPoint()
        {
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(Random.Range(areaMin.x, areaMax.x), 0f, Random.Range(areaMin.y, areaMax.y));
                if (Vector3.Distance(p, Flat(transform.position)) > 1.2f) return p;
            }
            return new Vector3((areaMin.x + areaMax.x) * 0.5f, 0f, (areaMin.y + areaMax.y) * 0.5f);
        }

        Vector3 Flat(Vector3 p) => new Vector3(p.x, transform.position.y, p.z);

        // ---- InteractableBehaviour ----
        public override string Prompt(PlayerInteractor player) => "E — " + dogName + "'ı sev";

        public override void Interact(PlayerInteractor who)
        {
            Pets++;
            petUntil = Time.time + 3f;
            if (state == State.Sleep || state == State.LieIdle || state == State.Lie) { Enter(State.StandUp, 1.05f); }
            else Enter(State.Happy, 3f);
            if (runtime == null) runtime = FindAnyObjectByType<CompanyRuntime>();
            if (runtime != null) runtime.Notify(dogName + " kuyruğunu sallıyor.");
        }
    }
}

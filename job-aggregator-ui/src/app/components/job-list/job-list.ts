import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { JobService, JobPost, SearchCriteriaDto, JobSearchResponse, FacebookGroup } from '../../services/job';
import { LOCATIONS } from './locations';

type ScraperTab = 'vieclam24h' | 'facebook';

@Component({
  selector: 'app-job-list', standalone: true, imports: [CommonModule, FormsModule],
  templateUrl: './job-list.html', styleUrls: ['./job-list.scss']
})
export class JobListComponent implements OnInit, OnDestroy {
  // ── Tab state ──────────────────────────────────────────────────────────
  activeTab: ScraperTab = 'vieclam24h';
  groups: FacebookGroup[] = [];
  selectedGroup?: FacebookGroup;
  groupPage = 1;
  groupsHaveMore = false;
  groupsLoading = false;
  groupMessage = '';
  fbSearchText = '';
  showScrapeForm = false;
  page = 1;
  hasMore = false;
  hasSearched = false;
  adminToken = '';
  isAdmin = true;
  runStatus = '';
  private scrapeKey?: string;
  private scrapePayload?: string;

  // ── Vieclam24h params ─────────────────────────────────────────────────
  vl24Keyword = '';
  vl24JobType = '';
  selectedProvince = '';
  selectedDistrict = '';
  provinces = LOCATIONS;
  districts: string[] = [];

  // ── Facebook params ───────────────────────────────────────────────────

  facebookGroupUrl = 'https://www.facebook.com/groups/sinhvientimvieclamthem/';
  facebookUrlHistory: string[] = [];
  showFacebookHistory = false;
  facebookUrlError = '';
  fbViewOption: 'CHRONOLOGICAL' | 'RECENT_ACTIVITY' | 'TOP_POSTS' | 'CHRONOLOGICAL_LISTINGS' = 'CHRONOLOGICAL';

  fbOnlyPostsNewerThan = '';
  private readonly facebookHistoryKey = 'job-aggregator.facebook-group-urls';

  // ── Shared params ─────────────────────────────────────────────────────
  maxJobs = 10;

  // ── Runtime state ─────────────────────────────────────────────────────
  jobs: JobPost[] = [];
  isLoading = false;
  message = '';
  expandedJobIds = new Set<string>();
  private ws?: WebSocket;
  private destroyed = false;
  private requestId: string | null = null;
  private generation = 0;
  private startedAt = 0;
  private requestedCount = 10;
  private timer?: ReturnType<typeof setTimeout>;
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private httpSubscription?: Subscription;
  private pollSubscription?: Subscription;

  constructor(private jobService: JobService, private zone: NgZone, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void { this.loadFacebookHistory(); this.connectWebSocket(); }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
    clearTimeout(this.reconnectTimer);
    this.httpSubscription?.unsubscribe();
    this.pollSubscription?.unsubscribe();
    this.ws?.close();
  }

  setTab(tab: ScraperTab): void {
    this.cancelView(); this.activeTab = tab; this.jobs = []; this.message = '';
    this.hasSearched = false; this.showScrapeForm = false; this.page = 1; this.hasMore = false;
    if (tab === 'facebook') this.loadGroups();
  }

  private cancelView(): void {
    ++this.generation; clearTimeout(this.timer); this.httpSubscription?.unsubscribe();
    this.pollSubscription?.unsubscribe(); this.isLoading = false; this.requestId = null; this.runStatus = '';
  }

  loadGroups(page = 1): void {
    this.groupsLoading = true; this.groupMessage = '';
    this.jobService.getGroups(page).subscribe({
      next: result => { if (this.destroyed) return; this.groups = result.groups; this.groupPage = result.page;
        this.groupsHaveMore = result.hasMore; this.groupsLoading = false; this.cdr.markForCheck(); },
      error: () => { if (this.destroyed) return; this.groupsLoading = false;
        this.groupMessage = 'Không thể tải danh sách nhóm. Vui lòng thử lại.'; this.cdr.markForCheck(); }
    });
  }

  selectGroup(group: FacebookGroup): void {
    this.cancelView(); this.selectedGroup = group; this.facebookGroupUrl = group.canonicalUrl;
    this.fbSearchText = ''; this.showScrapeForm = false; this.searchJobs();
  }

  newGroup(): void { this.cancelView(); this.selectedGroup = undefined; this.showScrapeForm = true; this.jobs = []; this.message = ''; }

  changePage(delta: number): void { this.searchJobs(Math.max(1, this.page + delta)); }
  startScrape(): void { if (this.isAdmin && !this.isLoading) this.submit(1, true); }
  searchJobs(page = 1): void { this.submit(page, false); }


  onProvinceChange(): void {
    this.selectedDistrict = '';
    this.districts = this.provinces.find(p => p.province === this.selectedProvince)?.districts ?? [];
  }

  // ── Facebook URL helpers ──────────────────────────────────────────────
  private normalizeFacebookUrl(value: string): string | null {
    try {
      const url = new URL(value.trim());
      if (url.protocol !== 'https:' || url.port || url.username || url.password ||
          !['facebook.com', 'www.facebook.com', 'm.facebook.com'].includes(url.hostname) ||
          !/^\/groups\/[A-Za-z0-9._-]+\/?$/.test(url.pathname)) return null;
      return 'https://www.facebook.com' + url.pathname.replace(/\/$/, '') + '/';
    } catch { return null; }
  }

  private loadFacebookHistory(): void {
    try {
      const saved: unknown = JSON.parse(localStorage.getItem(this.facebookHistoryKey) ?? '[]');
      if (Array.isArray(saved)) {
        this.facebookUrlHistory = [...new Set(saved.filter((url): url is string => typeof url === 'string')
          .map(url => this.normalizeFacebookUrl(url)).filter((url): url is string => url !== null))].slice(0, 10);
        this.facebookGroupUrl = this.facebookUrlHistory[0] ?? this.facebookGroupUrl;
      }
    } catch { /* Storage may be disabled or contain invalid JSON. */ }
  }

  selectFacebookUrl(url: string): void {
    this.facebookGroupUrl = url;
    this.facebookUrlError = '';
    this.showFacebookHistory = false;
  }

  closeFacebookHistory(event: FocusEvent): void {
    if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null))
      this.showFacebookHistory = false;
  }

  private rememberFacebookUrl(url: string): void {
    this.facebookUrlHistory = [url, ...this.facebookUrlHistory.filter(item => item !== url)].slice(0, 10);
    try { localStorage.setItem(this.facebookHistoryKey, JSON.stringify(this.facebookUrlHistory)); }
    catch { /* Search still works when browser storage is unavailable. */ }
  }

  // ── Search ─────────────────────────────────────────────────────────────
  private submit(page: number, scrape: boolean): void {
    let input: SearchCriteriaDto;

    if (this.activeTab === 'facebook') {

      if (!scrape && !this.selectedGroup) { this.message = 'Chọn nhóm để xem tin đã lưu.'; return; }
      const facebookUrl = this.normalizeFacebookUrl(this.facebookGroupUrl);
      this.facebookUrlError = '';
      if (!facebookUrl) {
        this.facebookUrlError = 'Nhập URL nhóm Facebook hợp lệ, ví dụ https://www.facebook.com/groups/ten-nhom/';
        return;
      }
      this.facebookGroupUrl = facebookUrl;
      this.rememberFacebookUrl(facebookUrl);
      this.showFacebookHistory = false;
      input = {
        Keyword: scrape ? '' : this.fbSearchText.trim(),
        FacebookGroupId: this.selectedGroup?.id,
        Location: '',
        JobType: '',
        Sources: ['facebook'],
        MaxJobs: Math.min(20, Math.max(1, this.maxJobs)),
        FacebookGroupUrl: facebookUrl,
        FacebookViewOption: this.fbViewOption,

        FacebookOnlyPostsNewerThan: this.fbOnlyPostsNewerThan || undefined,
      };
    } else {
      if (!this.vl24Keyword.trim()) { this.message = 'Vui lòng nhập từ khóa tìm kiếm.'; return; }
      input = {
        Keyword: this.vl24Keyword.trim(),
        Location: this.selectedDistrict ? `${this.selectedDistrict}, ${this.selectedProvince}` : this.selectedProvince,
        JobType: this.vl24JobType,
        Sources: ['vieclam24h'],
        MaxJobs: Math.min(20, Math.max(1, this.maxJobs)),
        Province: this.selectedProvince,
        District: this.selectedDistrict,
      };
    }

    input.Page = page; input.PageSize = 20;
    this.page = page; this.hasSearched = true;
    clearTimeout(this.timer);
    this.httpSubscription?.unsubscribe();
    this.pollSubscription?.unsubscribe();
    const generation = ++this.generation;
    this.requestId = null;
    this.startedAt = Date.now();
    this.requestedCount = input.MaxJobs;
    this.jobs = [];
    this.expandedJobIds.clear();
    this.isLoading = true;
    this.message = scrape ? 'Đang gửi yêu cầu cào...' : 'Đang tìm trong dữ liệu đã lưu...';

    const payload = JSON.stringify(input);
    if (scrape && (this.scrapePayload !== payload || !this.scrapeKey)) {
      this.scrapePayload = payload; this.scrapeKey = crypto.randomUUID();
    }
    const operation = scrape ? this.jobService.scrapeJobs(input, this.adminToken, this.scrapeKey!) : this.jobService.searchJobs(input);
    this.httpSubscription = operation.subscribe({
      next: response => {
        if (generation !== this.generation || this.destroyed) return;
        this.requestId = response.requestId;
        if (scrape) {
          this.scrapeKey = undefined; this.scrapePayload = undefined;
          if (response.facebookGroupId) this.selectedGroup = { id: response.facebookGroupId,
            name: this.selectedGroup?.name ?? this.facebookGroupUrl, canonicalUrl: this.facebookGroupUrl, jobCount: 0 };
        }
        this.applyResponse(response);
        if (this.isLoading) this.schedulePoll();
      },
      error: error => {
        if (generation !== this.generation || this.destroyed) return;
        this.isLoading = false;
        this.message = error?.error?.message || 'Không thể truy vấn dữ liệu. Vui lòng thử lại sau.';
        console.error('Search API failed', error);
        this.cdr.markForCheck();
      }
    });
  }

  // ── WebSocket ──────────────────────────────────────────────────────────
  connectWebSocket(): void {
    if (this.destroyed) return;
    this.ws = new WebSocket('wss://9imsgc5nqj.execute-api.ap-southeast-1.amazonaws.com/Prod');
    this.ws.onmessage = event => this.zone.run(() => {
      try {
        const data = JSON.parse(event.data);
        if (data.type === 'SEARCH_UPDATED' && data.requestId === this.requestId && this.isLoading)
          this.schedulePoll(0);
      } catch { /* Polling remains available if a notification cannot be parsed. */ }
    });
    this.ws.onclose = () => {
      if (!this.destroyed) this.reconnectTimer = setTimeout(() => this.connectWebSocket(), 5000);
    };
  }

  private applyResponse(response: JobSearchResponse): void {
    const unique = new Map<string, JobPost>();
    for (const job of response.jobs ?? []) unique.set(`${job.platform}:${job.externalId || job.id}`, job);
    this.jobs = [...unique.values()];
    this.page = response.page ?? 1; this.hasMore = response.hasMore ?? false;
    this.runStatus = response.status;
    this.isLoading = response.isScraping;
    this.message = response.message || (this.isLoading ? 'Đang cào và xử lý dữ liệu...'
      : this.jobs.length ? '' : 'Chưa có tin phù hợp trong dữ liệu đã lưu.');
    if (!this.isLoading && response.requestId && this.activeTab === 'facebook') this.loadGroups(this.groupPage);
    this.cdr.markForCheck();
  }

  private schedulePoll(delay = 3000): void {
    clearTimeout(this.timer);
    if (this.pollSubscription && !this.pollSubscription.closed) return;
    this.timer = setTimeout(() => this.poll(), delay);
  }

  private poll(): void {
    if (!this.requestId || !this.isLoading || this.destroyed) return;
    if (Date.now() - this.startedAt > 10 * 60 * 1000) {
      this.isLoading = false;
      this.message = 'Chưa nhận được kết quả cào bổ sung. Bạn có thể tìm lại; các việc làm đã có vẫn được giữ.';
      this.cdr.markForCheck();
      return;
    }
    const generation = this.generation;
    this.pollSubscription = this.jobService.getSearchStatus(this.requestId).subscribe({
      next: response => {
        if (generation === this.generation && !this.destroyed) this.applyResponse(response);
      },
      error: () => {
        if (generation !== this.generation || this.destroyed) return;
        this.message = 'Kết nối đang gián đoạn. Đang thử lấy lại kết quả; các việc làm đã có vẫn được giữ.';
        this.cdr.markForCheck();
        this.timer = setTimeout(() => this.poll(), 5000);
      },
      complete: () => {
        if (generation === this.generation && this.isLoading && !this.destroyed)
          this.timer = setTimeout(() => this.poll(), 3000);
      }
    });
  }

  retryProcessing(): void {
    if (!this.requestId || !this.isAdmin || this.isLoading) return;
    this.isLoading = true;
    this.httpSubscription = this.jobService.retryProcessing(this.requestId, this.adminToken).subscribe({
      next: response => { this.applyResponse(response); if (this.isLoading) this.schedulePoll(); },
      error: error => { this.isLoading = false; this.message = error?.error?.message || 'Không thể xử lý lại.'; this.cdr.markForCheck(); }
    });
  }

  toggleJob(id: string): void {
    if (this.expandedJobIds.has(id)) this.expandedJobIds.delete(id);
    else this.expandedJobIds.add(id);
  }

  get currentYear(): number { return new Date().getFullYear(); }
}

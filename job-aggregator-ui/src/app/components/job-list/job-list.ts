import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { JobService, JobPost, SearchCriteriaDto, JobSearchResponse } from '../../services/job';
import { LOCATIONS } from './locations';

@Component({
  selector: 'app-job-list', standalone: true, imports: [CommonModule, FormsModule],
  templateUrl: './job-list.html', styleUrls: ['./job-list.scss']
})
export class JobListComponent implements OnInit, OnDestroy {
  criteria: SearchCriteriaDto = { Keyword: '', Location: '', JobType: '', Sources: [], MaxJobs: 5 };
  selectedProvince = '';
  selectedDistrict = '';
  provinces = LOCATIONS;
  districts: string[] = [];
  sources = { chotot: false, facebook: false, vieclam24h: true, topcv: false };
  jobs: JobPost[] = [];
  isLoading = false;
  message = '';
  expandedJobIds = new Set<string>();
  private ws?: WebSocket;
  private destroyed = false;
  private requestId: string | null = null;
  private generation = 0;
  private startedAt = 0;
  private requestedCount = 5;
  private timer?: ReturnType<typeof setTimeout>;
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private httpSubscription?: Subscription;
  private pollSubscription?: Subscription;

  constructor(private jobService: JobService, private zone: NgZone, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void { this.connectWebSocket(); }
  onProvinceChange(): void {
    this.selectedDistrict = '';
    this.districts = this.provinces.find(p => p.province === this.selectedProvince)?.districts ?? [];
  }
  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
    clearTimeout(this.reconnectTimer);
    this.httpSubscription?.unsubscribe();
    this.pollSubscription?.unsubscribe();
    this.ws?.close();
  }
  connectWebSocket(): void {
    if (this.destroyed) return;
    this.ws = new WebSocket('wss://9imsgc5nqj.execute-api.ap-southeast-1.amazonaws.com/Prod');
    this.ws.onmessage = event => this.zone.run(() => {
      try {
        const data = JSON.parse(event.data);
        // WebSocket chỉ báo DB đã đổi cho đúng request; frontend sẽ GET lại dữ liệu mới.
        // requestId chặn kết quả đến muộn của lần tìm trước đi vào màn hình hiện tại.
        if (data.type === 'SEARCH_UPDATED' && data.requestId === this.requestId && this.isLoading)
          this.schedulePoll(0);
      } catch { /* Polling remains available if a notification cannot be parsed. */ }
    });
    this.ws.onclose = () => {
      if (!this.destroyed) this.reconnectTimer = setTimeout(() => this.connectWebSocket(), 5000);
    };
  }
  searchJobs(): void {
    if (!this.criteria.Keyword.trim()) return;
    clearTimeout(this.timer);
    this.httpSubscription?.unsubscribe();
    this.pollSubscription?.unsubscribe();
    const generation = ++this.generation;
    this.requestId = null;
    this.startedAt = Date.now();
    this.requestedCount = Math.min(20, Math.max(1, Number(this.criteria.MaxJobs) || 5));
    const selectedSources = Object.entries(this.sources).filter(([, enabled]) => enabled).map(([source]) => source);
    const input: SearchCriteriaDto = {
      ...this.criteria, Keyword: this.criteria.Keyword.trim(), MaxJobs: this.requestedCount,
      Province: this.selectedProvince, District: this.selectedDistrict,
      Location: this.selectedDistrict ? `${this.selectedDistrict}, ${this.selectedProvince}` : this.selectedProvince,
      Sources: selectedSources.length ? selectedSources : ['vieclam24h']
    };
    this.jobs = [];
    this.expandedJobIds.clear();
    this.isLoading = true;
    this.message = 'Đang tìm việc làm phù hợp trong dữ liệu đã có...';
    // Chỉ POST một lần; backend trả ngay record DB hợp lệ trước khi cào bù hoàn tất.
    this.httpSubscription = this.jobService.searchJobs(input).subscribe({
      next: response => {
        if (generation !== this.generation || this.destroyed) return;
        this.requestId = response.requestId;
        this.applyResponse(response);
        if (this.isLoading) this.schedulePoll();
      },
      error: error => {
        if (generation !== this.generation || this.destroyed) return;
        this.isLoading = false;
        this.message = 'Không thể truy vấn dữ liệu. Vui lòng thử lại sau.';
        console.error('Search API failed', error);
        this.cdr.markForCheck();
      }
    });
  }
  private applyResponse(response: JobSearchResponse): void {
    // Gộp theo nguồn + externalId, xếp ngày đăng mới nhất và giới hạn đúng số user yêu cầu.
    const unique = new Map<string, JobPost>();
    for (const job of response.jobs ?? []) unique.set(`${job.platform}:${job.externalId || job.id}`, job);
    this.jobs = [...unique.values()].sort((a, b) => b.postedDate.localeCompare(a.postedDate)).slice(0, this.requestedCount);
    this.isLoading = response.isScraping && this.jobs.length < this.requestedCount;
    if (response.message) this.message = response.message;
    else if (this.isLoading) this.message = this.jobs.length
      ? `Đã có ${this.jobs.length}/${this.requestedCount} việc làm. Đang tìm bổ sung phần còn thiếu...`
      : 'Đang cào dữ liệu mới, kết quả sẽ tự cập nhật tại đây...';
    else if (this.jobs.length < this.requestedCount) this.message = this.jobs.length
      ? `Tìm được ${this.jobs.length}/${this.requestedCount} việc làm phù hợp. Nguồn hiện chưa có thêm kết quả.`
      : 'Không tìm thấy việc làm phù hợp với yêu cầu tại khu vực này.';
    else this.message = '';
    this.cdr.markForCheck();
  }
  private schedulePoll(delay = 3000): void {
    clearTimeout(this.timer);
    if (this.pollSubscription && !this.pollSubscription.closed) return;
    this.timer = setTimeout(() => this.poll(), delay);
  }
  private poll(): void {
    // Polling dự phòng cho WebSocket: chỉ GET trạng thái và giữ job đang hiện nếu mạng lỗi.
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
  toggleJob(id: string): void {
    if (this.expandedJobIds.has(id)) this.expandedJobIds.delete(id);
    else this.expandedJobIds.add(id);
  }
}

# walkin
walkin unity file

## 이 pull 받은 뒤 꼭 해야 할 것 (2026-08-27)

**원격 아바타가 보라색(shader missing)으로 보이는 문제** 관련 — 아래 작업을 한 번 해주세요:

1. Unity에서 **Window > Package Manager** 열기
2. In Project 목록에서 **"Meta Avatars SDK"** 선택
3. 우측 상단 **Samples** 탭으로 이동
4. **"Avatar Sample Assets"** 옆의 **Import** 버튼 클릭 (또는 이미 Imported면 재확인만)

이 샘플을 임포트해야 로컬 fallback 아바타 에셋(zip, 약 490MB)이 이 컴퓨터에도 생성됩니다. 이 파일들은 용량 때문에 git에는 안 올렸고(`.gitignore`), 각자 Package Manager에서 직접 받아야 합니다.

### 이번 커밋에서 같이 고친 것
- Z키/컨트롤러 트리거로 실행하는 최적화 결과가 이제 다른 컴퓨터에도 네트워크로 전달되어 똑같이 반영됩니다 (`LocalOptimizationRunner.cs`, `TransferManager.cs`)
- 아바타가 ROI 원 중심에 정확히 서도록 joint 탐색 로직 수정 (`AvatarJointHelper.cs`)
- 씬에 빠져있던 Avatar SDK shader manager 프리팹 추가 (`SampleScene.unity`) — 위 1~4번 임포트와 함께 보라색 아바타 문제를 해결하기 위한 조치
